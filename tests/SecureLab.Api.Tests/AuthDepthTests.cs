using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SecureLab.Api.Data;
using SecureLab.Api.Data.Entities;

namespace SecureLab.Api.Tests;

/// <summary>Поглиблення відмінного рівня: готовність hasher до оновлення та додатковий auth defect (timing).</summary>
public sealed class AuthDepthTests(SecureLabApiFactory factory, Xunit.Abstractions.ITestOutputHelper output)
    : IClassFixture<SecureLabApiFactory>
{
    private sealed class NoCostGuard(IOptions<PasswordHasherOptions> options) : LoginTimingGuard(options)
    {
        // Відтворює стан до виправлення: невідомий користувач не платить вартість перевірки hash.
        public override void SpendVerificationCost(string password) { }
    }

    private static PasswordHasher<ApplicationUser> Hasher(int iterations) =>
        new(Options.Create(new PasswordHasherOptions { IterationCount = iterations }));

    [Fact]
    public void Hasher_ReportsRehashNeeded_ForHashCreatedWithWeakerParameters()
    {
        var user = new ApplicationUser { DisplayName = "x", UserName = "hasher-probe" };
        const string password = "Probe-Pass-1!a";

        var oldHash = Hasher(10_000).HashPassword(user, password);
        var currentHash = Hasher(100_000).HashPassword(user, password);

        Assert.Equal(PasswordVerificationResult.SuccessRehashNeeded,
            Hasher(100_000).VerifyHashedPassword(user, oldHash, password));
        Assert.Equal(PasswordVerificationResult.Success,
            Hasher(100_000).VerifyHashedPassword(user, currentHash, password));
        Assert.Equal(PasswordVerificationResult.Failed,
            Hasher(100_000).VerifyHashedPassword(user, oldHash, password + "x"));
        Assert.NotEqual(oldHash, currentHash);
    }

    [Fact]
    public async Task Login_UpgradesOldHash_AfterSuccessfulVerification()
    {
        using var client = factory.Anonymous();
        var userName = "rehash" + Guid.NewGuid().ToString("N")[..8];
        var password = TestPasswords.Seed;
        using (var register = await client.PostAsJsonAsync("/api/auth/register",
            new { userName, email = userName + "@example.test", displayName = "Rehash", password }))
            Assert.Equal(HttpStatusCode.Created, register.StatusCode);

        string oldHash;
        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = (await users.FindByNameAsync(userName))!;
            oldHash = Hasher(10_000).HashPassword(user, password); // штучний акаунт зі слабшими параметрами
            user.PasswordHash = oldHash;
            Assert.True((await users.UpdateAsync(user)).Succeeded);
        }

        using var login = await client.PostAsJsonAsync("/api/auth/login", new { userName, password });
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SecureLabDbContext>();
            var user = await db.Users.AsNoTracking().SingleAsync(u => u.UserName == userName);
            Assert.NotEqual(oldHash, user.PasswordHash);
            Assert.Equal(PasswordVerificationResult.Success,
                Hasher(100_000).VerifyHashedPassword(user, user.PasswordHash!, password));
            output.WriteLine("hash upgraded after login: true (values not printed)");
        }
    }

    [Fact]
    public async Task Timing_WithoutGuard_UnknownUserIsMeasurablyFaster_DemonstratesFixedDefect()
    {
        using var unguarded = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services => services.AddSingleton<LoginTimingGuard, NoCostGuard>()));
        using var client = unguarded.Anonymous();

        async Task<double> MeanMsAsync(Func<int, string> user)
        {
            using (await client.PostAsJsonAsync("/api/auth/login", new { userName = user(0), password = TestPasswords.Wrong })) { }
            var sw = System.Diagnostics.Stopwatch.StartNew();
            for (var i = 1; i <= 10; i++)
                using (await client.PostAsJsonAsync("/api/auth/login", new { userName = user(i), password = TestPasswords.Wrong })) { }
            return sw.Elapsed.TotalMilliseconds / 10;
        }

        var unknownMs = await MeanMsAsync(i => "ghost" + i);
        var knownMs = await MeanMsAsync(_ => "alice");
        output.WriteLine($"WITHOUT guard: unknown={unknownMs:F1} ms, known-wrong-password={knownMs:F1} ms");
        Assert.True(unknownMs < knownMs * 0.5, "Expected the unguarded flow to expose a timing difference.");
    }

    private async Task<string> RegisterAsync(HttpClient client)
    {
        var userName = "stamp" + Guid.NewGuid().ToString("N")[..8];
        using var register = await client.PostAsJsonAsync("/api/auth/register",
            new { userName, email = userName + "@example.test", displayName = "Stamp", password = TestPasswords.Seed });
        Assert.Equal(HttpStatusCode.Created, register.StatusCode);
        return userName;
    }

    [Fact]
    public async Task Logout_DoesNotRevokeACopiedTicket_UntilServerSideEvent_KnownLimit()
    {
        using var anonymous = factory.Anonymous();
        var userName = await RegisterAsync(anonymous);

        using var login = await anonymous.PostAsJsonAsync("/api/auth/login", new { userName, password = TestPasswords.Seed });
        var copied = Http.MainCookie(login)!.Split(';')[0]; // лише в пам'яті тесту, не друкується

        using (var logout = await anonymous.PostAsync("/api/auth/logout", null))
            Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);

        // Звичайний logout очищає cookie клієнта, але вже скопійований квиток ще чинний (межа поведінки).
        using var replay = factory.CreateClient(new WebApplicationFactoryClientOptions
        { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false, HandleCookies = false });
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/me");
        request.Headers.Add("Cookie", copied);
        using var response = await replay.SendAsync(request);
        output.WriteLine($"replay of copied ticket after plain logout: {(int)response.StatusCode}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task SecurityStampRotation_InvalidatesOldSession_AtNextValidation()
    {
        using var host = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
                // Для демонстрації перевірка stamp виконується на кожному запиті (стандартно — раз на 30 хв).
                services.Configure<SecurityStampValidatorOptions>(o => o.ValidationInterval = TimeSpan.Zero)));
        using var client = host.Anonymous();
        var userName = await RegisterAsync(client);
        using var login = await client.PostAsJsonAsync("/api/auth/login", new { userName, password = TestPasswords.Seed });
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);

        using (var before = await client.GetAsync("/api/me"))
            Assert.Equal(HttpStatusCode.OK, before.StatusCode);

        using (var scope = host.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = (await users.FindByNameAsync(userName))!;
            Assert.True((await users.UpdateSecurityStampAsync(user)).Succeeded); // серверна подія
        }

        using var after = await client.GetAsync("/api/me");
        Assert.Equal(HttpStatusCode.Unauthorized, after.StatusCode);

        // Новий вхід після події працює.
        using var again = await client.PostAsJsonAsync("/api/auth/login", new { userName, password = TestPasswords.Seed });
        Assert.Equal(HttpStatusCode.NoContent, again.StatusCode);
        using var ok = await client.GetAsync("/api/me");
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
    }
}
