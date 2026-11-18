using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SecureLab.Api.Application.Incidents;
using SecureLab.Api.Data;
using SecureLab.Api.Data.Entities;

namespace SecureLab.Api.Tests;

/// <summary>
/// ЛР 4 (захист, відмінний рівень): матриця Read для двох реалізацій правила "owner або Analyst".
/// Реалізація 1 — resource-based handler (IncidentAuthorizationHandler).
/// Реалізація 2 — query scoping у IncidentQueries.GetDetailsAsync (видно через HTTP details).
/// Обидві перевіряються проти НЕЗАЛЕЖНОЇ матриці, а не одна проти одної.
/// </summary>
public sealed class ReadMatrixTests(SecureLabApiFactory factory) : IClassFixture<SecureLabApiFactory>
{
    private static readonly string[] Users = ["alice", "bob", "morgan", "admin"];

    // Незалежна матриця, виведена з правила (owner або Analyst; admin без додаткового права - ні).
    // Рядок: користувач, чи дозволено P_A (власник Alice), чи дозволено P_B (власник Bob).
    public static TheoryData<string, bool, bool> Matrix => new()
    {
        { "alice",  true,  false },
        { "bob",    false, true  },
        { "morgan", true,  true  },
        { "admin",  false, false },
    };

    // ---------------------------------------------------------------- PAIR-WEB04 (disposable P_A / P_B)

    private async Task<(Guid PA, Guid PB)> CreatePairAsync()
    {
        using var alice = await factory.LoginAsync("alice", TestPasswords.Seed);
        using var bob = await factory.LoginAsync("bob", TestPasswords.Seed);
        return (await CreateAsync(alice, "PAIR-WEB04-A"), await CreateAsync(bob, "PAIR-WEB04-B"));
    }

    private static async Task<Guid> CreateAsync(HttpClient client, string label)
    {
        using var response = await client.PostAsJsonAsync("/api/incidents", new
        {
            title = $"{label}-{Guid.NewGuid():N}",
            description = "Disposable запис PAIR-WEB04 для матриці Read, довший за сорок символів.",
            severity = "Low",
            occurredAtUtc = DateTimeOffset.UtcNow.AddMinutes(-5)
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("id").GetGuid();
    }

    private async Task CleanupAsync(params Guid[] ids)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SecureLabDbContext>();
        await db.Incidents.Where(item => ids.Contains(item.Id)).ExecuteDeleteAsync();
    }

    // ---------------------------------------------------------------- спостереження

    /// <summary>HTTP: дозволено = 200. 404 (і будь-що інше) = заборонено. Bool з 404 буквально не порівнюємо.</summary>
    private static async Task<bool> IsOkAsync(HttpClient client, Guid id)
    {
        using var response = await client.GetAsync($"/api/incidents/{id}");
        return response.StatusCode == HttpStatusCode.OK;
    }

    private static ClaimsPrincipal Principal(string user)
    {
        var (id, role) = user switch
        {
            "alice" => (DbSeeder.AliceId, DbSeeder.ReporterRole),
            "bob" => (DbSeeder.BobId, DbSeeder.ReporterRole),
            "morgan" => (DbSeeder.MorganId, DbSeeder.AnalystRole),
            "admin" => (DbSeeder.AdminId, DbSeeder.AdministratorRole),
            _ => throw new ArgumentOutOfRangeException(nameof(user))
        };
        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, id.ToString()), new Claim(ClaimTypes.Role, role)],
            authenticationType: "Test");
        return new ClaimsPrincipal(identity);
    }

    private static Incident Resource(Guid id, Guid ownerId) => new()
    {
        Id = id, OwnerUserId = ownerId, Title = "t", Description = "d", Status = IncidentStatus.New
    };

    // ---------------------------------------------------------------- тести

    [Theory]
    [MemberData(nameof(Matrix))]
    public async Task Handler_MatchesMatrix(string user, bool expectedPA, bool expectedPB)
    {
        using var scope = factory.Services.CreateScope();
        var authz = scope.ServiceProvider.GetRequiredService<IAuthorizationService>();
        var principal = Principal(user);
        var pa = Resource(Guid.NewGuid(), DbSeeder.AliceId); // P_A: власник Alice
        var pb = Resource(Guid.NewGuid(), DbSeeder.BobId);   // P_B: власник Bob

        Assert.Equal(expectedPA, (await authz.AuthorizeAsync(principal, pa, IncidentOperations.Read)).Succeeded);
        Assert.Equal(expectedPB, (await authz.AuthorizeAsync(principal, pb, IncidentOperations.Read)).Succeeded);
    }

    [Theory]
    [MemberData(nameof(Matrix))]
    public async Task DetailsQuery_MatchesMatrix(string user, bool expectedPA, bool expectedPB)
    {
        var (pa, pb) = await CreatePairAsync();
        try
        {
            using var client = await factory.LoginAsync(user, TestPasswords.Seed);

            // Точні allowed-id sets: ids успішних (200) details-відповідей.
            var actual = new HashSet<Guid>();
            if (await IsOkAsync(client, pa)) actual.Add(pa);
            if (await IsOkAsync(client, pb)) actual.Add(pb);

            var expected = new HashSet<Guid>();
            if (expectedPA) expected.Add(pa);
            if (expectedPB) expected.Add(pb);

            Assert.True(expected.SetEquals(actual),
                $"{user}: очікувано allowed-set {{{Name(expected, pa, pb)}}}, фактично {{{Name(actual, pa, pb)}}}");
        }
        finally
        {
            await CleanupAsync(pa, pb);
        }
    }

    [Fact]
    public async Task Bob_AfterAlice_SameProcess_SeesOnlyOwn()
    {
        var (pa, pb) = await CreatePairAsync();
        try
        {
            using var alice = await factory.LoginAsync("alice", TestPasswords.Seed);
            using var bob = await factory.LoginAsync("bob", TestPasswords.Seed); // окремий authenticated client

            Assert.True(await IsOkAsync(alice, pa));   // Alice читає P_A
            Assert.False(await IsOkAsync(bob, pa));    // Bob -> P_A: deny (після Alice, той самий процес)
            Assert.True(await IsOkAsync(bob, pb));     // Bob -> P_B: allow
        }
        finally
        {
            await CleanupAsync(pa, pb);
        }
    }

    private static string Name(HashSet<Guid> set, Guid pa, Guid pb) =>
        string.Join(",", new[] { set.Contains(pa) ? "P_A" : null, set.Contains(pb) ? "P_B" : null }
            .Where(x => x is not null));
}
