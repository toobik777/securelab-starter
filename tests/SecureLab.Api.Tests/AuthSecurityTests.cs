using System.Net;
using Microsoft.AspNetCore.Hosting;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SecureLab.Api.Data;
using SecureLab.Api.Data.Entities;

namespace SecureLab.Api.Tests;

public static class TestPasswords
{
    public static string Seed => Environment.GetEnvironmentVariable("SeedUsers__Password")
        ?? throw new InvalidOperationException("Set local SeedUsers__Password outside Git.");

    // Інший (хибний) пароль формується локально й ніде не друкується.
    public static string Wrong => Seed + "-wrong";
}

internal static class Http
{
    public static HttpClient Anonymous(this WebApplicationFactory<Program> factory, string baseAddress = "https://localhost") =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri(baseAddress), AllowAutoRedirect = false, HandleCookies = true
        });

    public static object NewIncident(string title, Guid? forgedOwner = null)
    {
        var body = new Dictionary<string, object?>
        {
            ["title"] = title,
            ["description"] = "Опис для перевірки власника запису, довший за сорок символів.",
            ["severity"] = "Low",
            ["occurredAtUtc"] = DateTimeOffset.UtcNow.AddMinutes(-5)
        };
        if (forgedOwner is not null) body["ownerUserId"] = forgedOwner;
        return body;
    }

    public static string[] SetCookies(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var values) ? values.ToArray() : [];

    public static string? MainCookie(HttpResponseMessage response) =>
        SetCookies(response).FirstOrDefault(c => c.StartsWith(".AspNetCore.Identity.Application=", StringComparison.Ordinal));
}

/// <summary>Інтеграційні безпекові сценарії ЛР 3: A-01…A-07, T-01…T-03, S-01, cookie, rate limit.</summary>
public sealed class AuthSecurityTests(SecureLabApiFactory factory, Xunit.Abstractions.ITestOutputHelper output)
    : IClassFixture<SecureLabApiFactory>
{
    private async Task<Incident> ReadIncidentAsync(Guid id)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SecureLabDbContext>();
        return await db.Incidents.AsNoTracking().SingleAsync(i => i.Id == id);
    }

    private async Task DeleteIncidentAsync(Guid id)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SecureLabDbContext>();
        await db.Incidents.Where(i => i.Id == id).ExecuteDeleteAsync();
    }

    private async Task<Guid> CreateAsync(HttpClient client, string title, Guid? forgedOwner = null)
    {
        using var response = await client.PostAsJsonAsync("/api/incidents", Http.NewIncident(title, forgedOwner));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("id").GetGuid();
    }

    // ---------------------------------------------------------------- A-01, A-02, A-03

    [Fact]
    public async Task A01_AnonymousCreate_Returns401_NotRedirectNotHtml()
    {
        using var client = factory.Anonymous();
        using var response = await client.PostAsJsonAsync("/api/incidents", Http.NewIncident("A01-" + Guid.NewGuid().ToString("N")));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(response.Headers.Location);
        Assert.Empty(response.Headers.WwwAuthenticate);
        Assert.NotEqual("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task A01_AnonymousMe_Returns401()
    {
        using var client = factory.Anonymous();
        using var response = await client.GetAsync("/api/me");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(response.Headers.Location);
    }

    [Fact]
    public async Task A02_LoginThenMe_ReturnsPrincipalWithSafeFieldsOnly()
    {
        using var client = await factory.LoginAsync("alice", TestPasswords.Seed);
        using var response = await client.GetAsync("/api/me");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;
        Assert.Equal(DbSeeder.AliceId, root.GetProperty("userId").GetGuid());
        Assert.Equal("alice", root.GetProperty("userName").GetString());
        Assert.Contains("Reporter", root.GetProperty("roles").EnumerateArray().Select(r => r.GetString()));

        var names = root.EnumerateObject().Select(p => p.Name).Order().ToArray();
        Assert.Equal(new[] { "roles", "userId", "userName" }, names);
    }

    [Fact]
    public async Task A03_ForeignDemoHeader_DoesNotChangeCurrentUser()
    {
        using var client = await factory.LoginAsync("alice", TestPasswords.Seed);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/me");
        request.Headers.Add("X-Demo-UserId", DbSeeder.BobId.ToString());
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(DbSeeder.AliceId, json.RootElement.GetProperty("userId").GetGuid());
    }

    [Fact]
    public async Task A03_DemoHeaderWithoutCookie_Returns401()
    {
        using var client = factory.Anonymous();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/me");
        request.Headers.Add("X-Demo-UserId", DbSeeder.BobId.ToString());
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ---------------------------------------------------------------- A-04, A-05

    [Fact]
    public async Task A04_CreateWithoutOwner_TakesOwnerFromPrincipal()
    {
        using var client = await factory.LoginAsync("bob", TestPasswords.Seed);
        var id = await CreateAsync(client, "A04-" + Guid.NewGuid().ToString("N"));
        try
        {
            Assert.Equal(DbSeeder.BobId, (await ReadIncidentAsync(id)).OwnerUserId);
        }
        finally { await DeleteIncidentAsync(id); }
    }

    [Fact]
    public async Task A05_ForgedOwnerInJson_IsIgnored_OwnerStaysPrincipal()
    {
        using var client = await factory.LoginAsync("bob", TestPasswords.Seed);
        var id = await CreateAsync(client, "A05-" + Guid.NewGuid().ToString("N"), forgedOwner: DbSeeder.AliceId);
        try
        {
            var stored = await ReadIncidentAsync(id);
            Assert.Equal(DbSeeder.BobId, stored.OwnerUserId);
            Assert.NotEqual(DbSeeder.AliceId, stored.OwnerUserId);
        }
        finally { await DeleteIncidentAsync(id); }
    }

    // ---------------------------------------------------------------- A-06

    [Fact]
    public async Task A06_Logout_ThenProtectedRequest_Returns401()
    {
        using var client = await factory.LoginAsync("alice", TestPasswords.Seed);
        using (var before = await client.GetAsync("/api/me"))
            Assert.Equal(HttpStatusCode.OK, before.StatusCode);

        using var logout = await client.PostAsync("/api/auth/logout", null);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);

        // Основну cookie обираємо за назвою: заголовків Set-Cookie може бути кілька.
        var main = Http.MainCookie(logout);
        Assert.NotNull(main);
        Assert.Contains("expires=Thu, 01 Jan 1970", main, StringComparison.OrdinalIgnoreCase);

        using var after = await client.GetAsync("/api/me");
        Assert.Equal(HttpStatusCode.Unauthorized, after.StatusCode);
    }

    // ---------------------------------------------------------------- A-07

    private static async Task<(HttpStatusCode Status, string? ContentType, string Body)> ProblemBodyAsync(HttpResponseMessage response)
    {
        var raw = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(raw)) return (response.StatusCode, response.Content.Headers.ContentType?.MediaType, "");
        using var json = JsonDocument.Parse(raw);
        var stable = json.RootElement.EnumerateObject()
            .Where(p => p.Name != "traceId") // traceId відрізняється в кожному запиті
            .ToDictionary(p => p.Name, p => p.Value.ToString());
        return (response.StatusCode, response.Content.Headers.ContentType?.MediaType,
            JsonSerializer.Serialize(stable.OrderBy(kv => kv.Key).ToArray()));
    }

    [Fact]
    public async Task A07_UnknownUserAndWrongPassword_HaveIdenticalExternalResult()
    {
        using var client = factory.Anonymous();
        using var unknown = await client.PostAsJsonAsync("/api/auth/login",
            new { userName = "nobody" + Guid.NewGuid().ToString("N")[..8], password = TestPasswords.Wrong });
        using var wrong = await client.PostAsJsonAsync("/api/auth/login",
            new { userName = "alice", password = TestPasswords.Wrong });

        Assert.Equal(HttpStatusCode.Unauthorized, unknown.StatusCode);
        Assert.Equal(await ProblemBodyAsync(unknown), await ProblemBodyAsync(wrong));
        Assert.Empty(Http.SetCookies(unknown));
        Assert.Empty(Http.SetCookies(wrong));
    }

    [Fact]
    public async Task A07_Timing_UnknownUserPaysHashingCost()
    {
        using var client = factory.Anonymous();
        async Task<double> MeanMsAsync(Func<int, string> user)
        {
            await client.PostAsJsonAsync("/api/auth/login", new { userName = user(0), password = TestPasswords.Wrong }); // прогрів
            var sw = System.Diagnostics.Stopwatch.StartNew();
            for (var i = 1; i <= 10; i++)
                using (await client.PostAsJsonAsync("/api/auth/login", new { userName = user(i), password = TestPasswords.Wrong })) { }
            return sw.Elapsed.TotalMilliseconds / 10;
        }

        var unknownMs = await MeanMsAsync(i => "ghost" + i);
        var knownMs = await MeanMsAsync(_ => "alice");
        output.WriteLine($"mean login failure time: unknown={unknownMs:F1} ms, known-wrong-password={knownMs:F1} ms");
        // До виправлення невідомий користувач відповідав на порядок швидше (без перевірки hash).
        Assert.True(unknownMs > knownMs * 0.5, "Unknown user login is measurably faster than known user.");
    }

    // ---------------------------------------------------------------- T-01, T-02, T-03

    private static object NewRegistration(string userName, string password, string? email = null) =>
        new { userName, email = email ?? userName + "@example.test", displayName = "Студент " + userName, password };

    private static string NewUserName() => "reg" + Guid.NewGuid().ToString("N")[..8];

    [Fact]
    public async Task T01_Register_Valid_Returns201_WithSafeFieldsOnly_AndRoleIgnoredFromBody()
    {
        using var client = factory.Anonymous();
        var userName = NewUserName();
        var password = TestPasswords.Seed;
        var body = new
        {
            userName, email = userName + "@example.test", displayName = "Студент", password,
            role = "Administrator", id = Guid.NewGuid() // зайві поля мають ігноруватися
        };
        using var response = await client.PostAsJsonAsync("/api/auth/register", body);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("/api/me", response.Headers.Location?.OriginalString);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var names = json.RootElement.EnumerateObject().Select(p => p.Name).Order().ToArray();
        Assert.Equal(new[] { "displayName", "id", "userName" }, names);
        Assert.NotEqual(body.id, json.RootElement.GetProperty("id").GetGuid());

        // Роль із body не вплинула: новий користувач лише Reporter.
        using var session = await factory.LoginAsync(userName, password);
        using var me = await session.GetAsync("/api/me");
        using var meJson = JsonDocument.Parse(await me.Content.ReadAsStringAsync());
        Assert.Equal(new[] { "Reporter" }, meJson.RootElement.GetProperty("roles").EnumerateArray().Select(r => r.GetString()).ToArray());
    }

    [Fact]
    public async Task T02_Register_Duplicate_Returns409_And_InvalidInputReturns400()
    {
        using var client = factory.Anonymous();
        var userName = NewUserName();
        using (var first = await client.PostAsJsonAsync("/api/auth/register", NewRegistration(userName, TestPasswords.Seed)))
            Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        // Повторна реєстрація (те саме ім'я) і (інше ім'я, той самий email) дають однаковий 409.
        using var dupName = await client.PostAsJsonAsync("/api/auth/register", NewRegistration(userName, TestPasswords.Seed));
        using var dupEmail = await client.PostAsJsonAsync("/api/auth/register",
            NewRegistration(NewUserName(), TestPasswords.Seed, email: userName + "@example.test"));
        Assert.Equal(HttpStatusCode.Conflict, dupName.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, dupEmail.StatusCode);
        Assert.Equal(await ProblemBodyAsync(dupName), await ProblemBodyAsync(dupEmail));

        // Слабкий пароль, хибний email, задовгий DisplayName, відсутнє поле, недозволені символи.
        var bad = new object[]
        {
            NewRegistration(NewUserName(), "short1!A"),
            NewRegistration(NewUserName(), TestPasswords.Seed, email: "not-an-email"),
            new { userName = NewUserName(), email = "x@example.test", displayName = new string('Д', 121), password = TestPasswords.Seed },
            new { userName = NewUserName(), email = "y@example.test", password = TestPasswords.Seed },
            NewRegistration("bad name!", TestPasswords.Seed),
        };
        foreach (var payload in bad)
        {
            using var response = await client.PostAsJsonAsync("/api/auth/register", payload);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var text = await response.Content.ReadAsStringAsync();
            Assert.DoesNotContain(TestPasswords.Seed, text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task T03_StoredPassword_IsIdentityV3Hash_NotPlaintext_NoPlainPasswordColumn()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SecureLabDbContext>();
        var seed = TestPasswords.Seed;

        var hashes = await db.Users.AsNoTracking()
            .Where(u => u.Id == DbSeeder.AliceId || u.Id == DbSeeder.BobId)
            .OrderBy(u => u.UserName).Select(u => u.PasswordHash!).ToListAsync();
        Assert.Equal(2, hashes.Count);

        foreach (var hash in hashes)
        {
            Assert.NotEqual(seed, hash);
            Assert.DoesNotContain(seed, hash, StringComparison.Ordinal);
            var raw = Convert.FromBase64String(hash);
            Assert.Equal(0x01, raw[0]); // позначка формату Identity V3
            Assert.Equal(84, hash.Length);
        }
        // Різна сіль: однаковий навчальний пароль дає різні збережені представлення.
        Assert.NotEqual(hashes[0], hashes[1]);

        var columns = await db.Database
            .SqlQueryRaw<string>("select column_name as \"Value\" from information_schema.columns where table_name = 'identity_users'")
            .ToListAsync();
        output.WriteLine("identity_users columns: " + string.Join(", ", columns.Order()));
        Assert.Contains("PasswordHash", columns);
        Assert.DoesNotContain(columns, c => c.Equals("Password", StringComparison.OrdinalIgnoreCase));
    }

    // ---------------------------------------------------------------- S-01

    [Fact]
    public async Task S01_AuthEvents_DoNotContainCredentials()
    {
        var typoName = "typo" + Guid.NewGuid().ToString("N")[..8];
        var marker = "Zz9-" + Guid.NewGuid().ToString("N")[..10] + "!a"; // пароль-маркер для цього тесту
        using var client = factory.Anonymous();
        using (await client.PostAsJsonAsync("/api/auth/login", new { userName = typoName, password = marker })) { }
        using (await client.PostAsJsonAsync("/api/auth/login", new { userName = "alice", password = marker })) { }
        using (await client.PostAsJsonAsync("/api/auth/register", NewRegistration(NewUserName(), marker))) { }
        using var ok = await factory.LoginAsync("alice", TestPasswords.Seed);

        var logs = CapturedLogs.Snapshot();
        var authEvents = logs.Where(m => m.StartsWith("Auth event:", StringComparison.Ordinal)).ToArray();
        Assert.Contains(authEvents, m => m.Contains("login failed"));
        Assert.Contains(authEvents, m => m.Contains("login succeeded"));
        Assert.DoesNotContain(logs, m => m.Contains(marker, StringComparison.Ordinal));
        Assert.DoesNotContain(logs, m => m.Contains(TestPasswords.Seed, StringComparison.Ordinal));
        Assert.DoesNotContain(logs, m => m.Contains(typoName, StringComparison.Ordinal));
        Assert.DoesNotContain(logs, m => m.Contains("AQAAAA", StringComparison.Ordinal)); // префікс Identity V3 hash
        Assert.DoesNotContain(logs, m => m.Contains(".AspNetCore.Identity.Application=", StringComparison.Ordinal));
        output.WriteLine($"auth event lines checked: {authEvents.Length}");
    }

    [Fact]
    public async Task S01_ResponsesDoNotLeakCredentials()
    {
        using var client = factory.Anonymous();
        var userName = NewUserName();
        using var register = await client.PostAsJsonAsync("/api/auth/register", NewRegistration(userName, TestPasswords.Seed));
        var registerText = await register.Content.ReadAsStringAsync();
        using var session = await factory.LoginAsync(userName, TestPasswords.Seed);
        using var me = await session.GetAsync("/api/me");
        var meText = await me.Content.ReadAsStringAsync();

        foreach (var text in new[] { registerText, meText })
        {
            Assert.DoesNotContain(TestPasswords.Seed, text, StringComparison.Ordinal);
            foreach (var forbidden in new[] { "passwordHash", "securityStamp", "concurrencyStamp", "token", "password" })
                Assert.DoesNotContain(forbidden, text, StringComparison.OrdinalIgnoreCase);
        }
    }

    // ---------------------------------------------------------------- Cookie flags

    [Fact]
    public async Task Cookie_Https_HasHttpOnlySecureSameSiteLax_AndIsSessionCookie()
    {
        using var client = factory.Anonymous("https://localhost");
        using var response = await client.PostAsJsonAsync("/api/auth/login", new { userName = "alice", password = TestPasswords.Seed });
        var cookie = Http.MainCookie(response);
        Assert.NotNull(cookie);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("expires=", cookie, StringComparison.OrdinalIgnoreCase); // сеансова
        Assert.DoesNotContain("max-age=", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Cookie_LocalHttpDevelopment_HasNoSecure_ThatIsNotAProofOfSecure()
    {
        using var client = factory.Anonymous("http://localhost");
        using var response = await client.PostAsJsonAsync("/api/auth/login", new { userName = "alice", password = TestPasswords.Seed });
        var cookie = Http.MainCookie(response);
        Assert.NotNull(cookie);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secure", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Cookie_ProductionProfile_IsSecureEvenOverHttpRequest()
    {
        var connection = factory.Services.GetRequiredService<IConfiguration>()["ConnectionStrings:SecureLab"]!;
        using var production = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("ConnectionStrings:SecureLab", connection);
        });
        using var client = production.Anonymous("http://localhost");
        using var response = await client.PostAsJsonAsync("/api/auth/login", new { userName = "alice", password = TestPasswords.Seed });
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var cookie = Http.MainCookie(response);
        Assert.NotNull(cookie);
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", cookie, StringComparison.OrdinalIgnoreCase);
    }

    // ---------------------------------------------------------------- Rate limit

    [Fact]
    public async Task Login_RateLimit_Returns429_WithoutRevealingAccount_AndRecoversAfterWindow()
    {
        using var limited = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Lab03:Login:PermitLimit", "3");
            builder.UseSetting("Lab03:Login:WindowSeconds", "2");
        });
        using var client = limited.Anonymous();

        for (var i = 0; i < 3; i++)
        {
            using var attempt = await client.PostAsJsonAsync("/api/auth/login", new { userName = "alice", password = TestPasswords.Wrong });
            Assert.Equal(HttpStatusCode.Unauthorized, attempt.StatusCode);
        }
        using var knownBlocked = await client.PostAsJsonAsync("/api/auth/login", new { userName = "alice", password = TestPasswords.Seed });
        using var unknownBlocked = await client.PostAsJsonAsync("/api/auth/login", new { userName = "ghost", password = TestPasswords.Wrong });

        Assert.Equal(HttpStatusCode.TooManyRequests, knownBlocked.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, unknownBlocked.StatusCode);
        Assert.True(knownBlocked.Headers.Contains("Retry-After"));
        Assert.Equal(await knownBlocked.Content.ReadAsStringAsync(), await unknownBlocked.Content.ReadAsStringAsync());
        Assert.Empty(Http.SetCookies(knownBlocked)); // навіть правильний пароль не дає cookie під час блокування

        await Task.Delay(TimeSpan.FromSeconds(2.5));
        using var recovered = await client.PostAsJsonAsync("/api/auth/login", new { userName = "alice", password = TestPasswords.Seed });
        Assert.Equal(HttpStatusCode.NoContent, recovered.StatusCode);
        Assert.Contains(CapturedLogs.Snapshot(), m => m.StartsWith("Auth event: rate limit exceeded", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Register_RateLimit_Returns429()
    {
        using var limited = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Lab03:Register:PermitLimit", "2");
            builder.UseSetting("Lab03:Register:WindowSeconds", "30");
        });
        using var client = limited.Anonymous();
        for (var i = 0; i < 2; i++)
        {
            using var ok = await client.PostAsJsonAsync("/api/auth/register", NewRegistration(NewUserName(), TestPasswords.Seed));
            Assert.Equal(HttpStatusCode.Created, ok.StatusCode);
        }
        using var blocked = await client.PostAsJsonAsync("/api/auth/register", NewRegistration(NewUserName(), TestPasswords.Seed));
        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
    }
}
