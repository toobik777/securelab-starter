using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SecureLab.Api.Data;
using SecureLab.Api.Data.Entities;

namespace SecureLab.Api.Tests;

/// <summary>ЛР 6: antiforgery-периметр, REG-CSRF-01, заголовки на всіх класах відповідей, search limiter, profiles.</summary>
public sealed class Lab06SecurityTests(SecureLabApiFactory factory, Xunit.Abstractions.ITestOutputHelper output)
    : IClassFixture<SecureLabApiFactory>
{
    private const string UnreachableDb =
        "Host=127.0.0.1;Port=54399;Database=securelab;Username=securelab;Password=unreachable-study-value;Include Error Detail=false";

    private static object NewIncident(string title) => new
    {
        title,
        description = "Навчальний запис для перевірки antiforgery, довший за сорок символів.",
        severity = "Low",
        occurredAtUtc = DateTimeOffset.UtcNow.AddMinutes(-5)
    };

    private async Task<Guid> CreateAsync(HttpClient clientWithToken, string label)
    {
        using var response = await clientWithToken.PostAsJsonAsync("/api/incidents", NewIncident($"{label}-{Guid.NewGuid():N}"));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("id").GetGuid();
    }

    private async Task<Incident?> ReadAsync(Guid id)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SecureLabDbContext>();
        return await db.Incidents.AsNoTracking().SingleOrDefaultAsync(item => item.Id == id);
    }

    private async Task CleanupAsync(params Guid[] ids)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SecureLabDbContext>();
        await db.Incidents.Where(item => ids.Contains(item.Id)).ExecuteDeleteAsync();
    }

    private static async Task AssertAntiforgeryRejectionAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Antiforgery token is missing or invalid.", json.RootElement.GetProperty("title").GetString());
    }

    // ------------------------------------------------------------------ REG-CSRF-01 (автоматизований, повний)

    [Fact]
    public async Task RegCsrf01_FullFlow_LoginThenDeniedWithoutToken_ThenTokenThenAllowedChange()
    {
        using var alice = await factory.LoginAsync("alice", TestPasswords.Seed);   // cookie-сеанс без токена
        await alice.UseAntiforgeryTokenAsync();                                    // токен лише для створення фікстури
        var id = await CreateAsync(alice, "REG-CSRF-01");
        alice.DefaultRequestHeaders.Remove(CookieClient.TokenHeader);              // негативна частина: без токена
        try
        {
            var before = await ReadAsync(id);

            // 1. Відмова без токена; auth-cookie є.
            using var rejected = await alice.PutAsJsonAsync($"/api/incidents/{id}", new { title = "змінено без токена" });
            await AssertAntiforgeryRejectionAsync(rejected);

            // 2. Невалідний токен — та сама відмова.
            using var invalidRequest = new HttpRequestMessage(HttpMethod.Put, $"/api/incidents/{id}")
            {
                Content = JsonContent.Create(new { title = "змінено з невалідним токеном" })
            };
            invalidRequest.Headers.Add(CookieClient.TokenHeader, "invalid-test-value");
            using var invalid = await alice.SendAsync(invalidRequest);
            await AssertAntiforgeryRejectionAsync(invalid);

            // 3. Читання стану: ресурс не змінився.
            var afterRejected = await ReadAsync(id);
            Assert.Equal(before!.Title, afterRejected!.Title);
            Assert.Equal(before.UpdatedAtUtc, afterRejected.UpdatedAtUtc);

            // 4. Видача токена тим самим cookie-сеансом і дозволена зміна.
            await alice.UseAntiforgeryTokenAsync();
            using var accepted = await alice.PutAsJsonAsync($"/api/incidents/{id}", new { title = "змінено з валідним токеном" });
            Assert.Equal(HttpStatusCode.NoContent, accepted.StatusCode);

            // 5. Читання зміненого стану: саме очікувана зміна.
            var afterAccepted = await ReadAsync(id);
            Assert.Equal("змінено з валідним токеном", afterAccepted!.Title);
            Assert.Equal(DbSeeder.AliceId, afterAccepted.OwnerUserId);
        }
        finally { await CleanupAsync(id); }
    }

    // ------------------------------------------------------------------ периметр SC-01…SC-05

    [Fact]
    public async Task Perimeter_AllCookieAuthenticatedMutations_RejectMissingToken_WithSameResponse_AndKeepState()
    {
        using var alice = await factory.LoginWithTokenAsync("alice", TestPasswords.Seed);
        using var morgan = await factory.LoginWithTokenAsync("morgan", TestPasswords.Seed);
        var forDelete = await CreateAsync(alice, "SC-delete");
        var forUpdate = await CreateAsync(alice, "SC-update");
        var forTriage = await CreateAsync(alice, "SC-triage");
        try
        {
            // Клієнти без токена, але з валідним cookie-сеансом (окремі login).
            using var aliceNoToken = await factory.LoginAsync("alice", TestPasswords.Seed);
            using var morganNoToken = await factory.LoginAsync("morgan", TestPasswords.Seed);

            using var create = await aliceNoToken.PostAsJsonAsync("/api/incidents", NewIncident("SC-create-without-token"));
            using var update = await aliceNoToken.PutAsJsonAsync($"/api/incidents/{forUpdate}", new { title = "x" });
            using var delete = await aliceNoToken.DeleteAsync($"/api/incidents/{forDelete}");
            using var triage = await morganNoToken.PostAsync($"/api/incidents/{forTriage}/triage", null);
            using var logout = await aliceNoToken.PostAsync("/api/auth/logout", null);

            foreach (var response in new[] { create, update, delete, triage, logout })
            {
                await AssertAntiforgeryRejectionAsync(response); // один і той самий exact 4xx
            }

            // Стан не змінено: ресурси на місці, status New, сеанс після відхиленого logout чинний.
            Assert.NotNull(await ReadAsync(forDelete));
            Assert.NotEqual("x", (await ReadAsync(forUpdate))!.Title);
            Assert.Equal(IncidentStatus.New, (await ReadAsync(forTriage))!.Status);
            using var me = await aliceNoToken.GetAsync("/api/me");
            Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        }
        finally { await CleanupAsync(forDelete, forUpdate, forTriage); }
    }

    [Fact]
    public async Task Perimeter_TokenOfAnotherUser_IsRejected_AndAnonymousStillGets401()
    {
        using var alice = await factory.LoginWithTokenAsync("alice", TestPasswords.Seed);
        using var bob = await factory.LoginAsync("bob", TestPasswords.Seed);
        var id = await CreateAsync(alice, "SC-foreign-token");
        try
        {
            // Токен alice разом із cookie bob не проходить валідацію (токен прив'язано до identity).
            bob.DefaultRequestHeaders.Add(CookieClient.TokenHeader, alice.DefaultRequestHeaders.GetValues(CookieClient.TokenHeader).Single());
            using var response = await bob.PostAsJsonAsync("/api/incidents", NewIncident("SC-bob-with-alice-token"));
            await AssertAntiforgeryRejectionAsync(response);

            // Анонімний запит: authentication виконується раніше (401), а не antiforgery (400).
            using var anonymous = factory.Anonymous();
            using var put = await anonymous.PutAsJsonAsync($"/api/incidents/{id}", new { title = "x" });
            using var token = await anonymous.GetAsync(CookieClient.TokenRoute);
            Assert.Equal(HttpStatusCode.Unauthorized, put.StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, token.StatusCode);
        }
        finally { await CleanupAsync(id); }
    }

    [Fact]
    public async Task LegitimateFlow_WithValidToken_AllMutationsStillWork_IncludingLogout()
    {
        using var alice = await factory.LoginWithTokenAsync("alice", TestPasswords.Seed);
        var id = await CreateAsync(alice, "SC-positive");
        try
        {
            using var update = await alice.PutAsJsonAsync($"/api/incidents/{id}", new { severity = "High", description = new string('о', 45) });
            using var delete = await alice.DeleteAsync($"/api/incidents/{id}");
            using var logout = await alice.PostAsync("/api/auth/logout", null);

            Assert.Equal(HttpStatusCode.NoContent, update.StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
            Assert.Null(await ReadAsync(id));
            using var after = await alice.GetAsync("/api/me");
            Assert.Equal(HttpStatusCode.Unauthorized, after.StatusCode);
        }
        finally { await CleanupAsync(id); }
    }

    // ------------------------------------------------------------------ заголовки на п'яти класах відповідей

    private static void AssertSecurityHeaders(HttpResponseMessage response, bool apiPath)
    {
        Assert.Contains("nosniff", response.Headers.GetValues("X-Content-Type-Options"));
        Assert.Contains("no-referrer", response.Headers.GetValues("Referrer-Policy"));
        Assert.Contains("DENY", response.Headers.GetValues("X-Frame-Options"));
        var csp = response.Headers.GetValues("Content-Security-Policy").Single();
        Assert.Contains("frame-ancestors 'none'", csp);
        Assert.Contains("script-src 'self'", csp);
        Assert.DoesNotContain("unsafe-inline", csp);
        Assert.DoesNotContain("*", csp);
        if (apiPath)
        {
            Assert.Contains("no-store", response.Headers.CacheControl?.ToString() ?? string.Empty);
        }
    }

    [Fact]
    public async Task Headers_OnStatic_Success_Validation_NotFound_AndUnknownApiPath()
    {
        using var alice = await factory.LoginAsync("alice", TestPasswords.Seed);
        using var anonymous = factory.Anonymous();

        using var staticPage = await anonymous.GetAsync("/");
        using var success = await alice.GetAsync($"/api/incidents/{DbSeeder.AliceIncidentId}");
        using var validation = await alice.GetAsync("/api/incidents?status=Unknown");
        using var notFound = await alice.GetAsync($"/api/incidents/{Guid.NewGuid()}");
        using var unknownApi = await alice.GetAsync("/api/nonexistent-route");

        Assert.Equal(HttpStatusCode.OK, staticPage.StatusCode);
        Assert.Equal(HttpStatusCode.OK, success.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, validation.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, notFound.StatusCode);
        AssertSecurityHeaders(staticPage, apiPath: false);
        AssertSecurityHeaders(success, apiPath: true);
        AssertSecurityHeaders(validation, apiPath: true);
        AssertSecurityHeaders(notFound, apiPath: true);

        // F-03: невідомий API-шлях — 404 Problem Details, а не SPA-fallback 200 text/html.
        Assert.Equal(HttpStatusCode.NotFound, unknownApi.StatusCode);
        Assert.Equal("application/problem+json", unknownApi.Content.Headers.ContentType?.MediaType);
        AssertSecurityHeaders(unknownApi, apiPath: true);
    }

    // ------------------------------------------------------------------ production-like profile + контрольована 500

    private WebApplicationFactory<Program> ProductionHost(string connectionString) =>
        factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("ConnectionStrings:SecureLab", connectionString);
        });

    [Fact]
    public async Task Production_UnexpectedError_IsSafeProblemDetails_WithSecurityHeadersAndTraceId()
    {
        using var production = ProductionHost(UnreachableDb);
        Assert.Equal("Production", production.Services.GetRequiredService<IHostEnvironment>().EnvironmentName);
        using var client = production.Anonymous();

        using var response = await client.PostAsJsonAsync("/api/auth/login",
            new { userName = "alice", password = "NotARealPassword1!" });

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);
        Assert.Matches("^00-[0-9a-f]{32}-[0-9a-f]{16}-[0-9a-f]{2}$", json.RootElement.GetProperty("traceId").GetString());
        foreach (var forbidden in new[] { "Npgsql", "Exception", "StackTrace", "   at ", "54399", "unreachable-study-value", "Password", "Host=", ".cs:line" })
        {
            Assert.DoesNotContain(forbidden, body, StringComparison.OrdinalIgnoreCase);
        }

        // F-01: заголовки безпеки є й на error path (ExceptionHandler очищає заголовки до запису 500).
        AssertSecurityHeaders(response, apiPath: true);
        output.WriteLine("500 safe: status=500, traceId present, security headers present");
    }

    [Fact]
    public async Task Production_Profile_DiffersFromDevelopment_OpenApiAndCorsNotRegistered()
    {
        using var production = ProductionHost(UnreachableDb);
        using var client = production.Anonymous();

        using var openApi = await client.GetAsync("/openapi/v1.json");
        Assert.Equal(HttpStatusCode.NotFound, openApi.StatusCode); // MapOpenApi() лише в Development

        // CORS policy Lab05BrowserProbe реєструється лише в Development: у Production allow-origin немає.
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.Add("Origin", "http://localhost:4173");
        using var cors = await client.SendAsync(request);
        Assert.False(cors.Headers.Contains("Access-Control-Allow-Origin"));

        using var development = factory.Anonymous();
        using var devOpenApi = await development.GetAsync("/openapi/v1.json");
        Assert.Equal(HttpStatusCode.OK, devOpenApi.StatusCode);
    }

    // ------------------------------------------------------------------ audit trail та Host filtering

    [Fact]
    public async Task Audit_AllowedPrivilegedTransition_IsRecorded_WithoutSecrets()
    {
        using var alice = await factory.LoginWithTokenAsync("alice", TestPasswords.Seed);
        using var morgan = await factory.LoginWithTokenAsync("morgan", TestPasswords.Seed);
        var id = await CreateAsync(alice, "AUDIT-allowed");
        try
        {
            using var response = await morgan.PostAsync($"/api/incidents/{id}/triage", null);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var line = Assert.Single(CapturedLogs.Snapshot(), entry =>
                entry.StartsWith("authorization.allowed", StringComparison.Ordinal)
                && entry.Contains(id.ToString(), StringComparison.Ordinal));
            output.WriteLine(line);
            Assert.Contains($"subject={DbSeeder.MorganId}", line);
            Assert.Contains("action=incident.triage", line);
            Assert.Contains("result=allowed", line);
            Assert.Contains("from=New to=Triaged", line);
            Assert.Contains("occurredAtUtc=", line);
            Assert.Contains("traceId=", line);
            foreach (var forbidden in new[] { TestPasswords.Seed, ".AspNetCore.Identity", "Cookie", "password", "X-CSRF-TOKEN" })
            {
                Assert.DoesNotContain(forbidden, line, StringComparison.OrdinalIgnoreCase);
            }
        }
        finally { await CleanupAsync(id); }
    }

    [Fact]
    public async Task HostFiltering_UnknownHostHeader_IsRejected_ButLocalhostWorks()
    {
        using var client = factory.Anonymous();
        using var ok = await client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.Host = "evil.example";
        using var rejected = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
    }

    // ------------------------------------------------------------------ search rate limiting (кілька partitions)

    [Fact]
    public async Task SearchRateLimit_Partitions_Quota_429_AndRecovery()
    {
        using var limited = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Lab06:Search:PermitLimit", "3");
            builder.UseSetting("Lab06:Search:WindowSeconds", "2");
        });
        using var alice = await limited.LoginAsync("alice", TestPasswords.Seed);
        using var bob = await limited.LoginAsync("bob", TestPasswords.Seed);
        using var anonymous = limited.Anonymous();

        // У межах quota поведінка не змінюється.
        for (var i = 0; i < 3; i++)
        {
            using var ok = await alice.GetAsync("/api/incidents/search?q=%D0%BD%D0%B0%D0%B2%D1%87");
            Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        }

        // Наступна спроба: 429 із Retry-After.
        using var blocked = await alice.GetAsync("/api/incidents/search?q=a");
        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
        Assert.True(blocked.Headers.Contains("Retry-After"));

        // Інший partition (bob) має власний лічильник; авторизація лишається окремою (bob бачить лише свої записи).
        using var bobSearch = await bob.GetAsync("/api/incidents/search?q=O%27Brien");
        Assert.Equal(HttpStatusCode.OK, bobSearch.StatusCode);

        // Анонім: partition за адресою; rate limit не підміняє authentication (далі 401, не 200).
        using var anon = await anonymous.GetAsync("/api/incidents/search?q=a");
        Assert.Equal(HttpStatusCode.Unauthorized, anon.StatusCode);

        // Після вікна легітимний запит знову можливий.
        await Task.Delay(TimeSpan.FromSeconds(2.5));
        using var recovered = await alice.GetAsync("/api/incidents/search?q=a");
        Assert.Equal(HttpStatusCode.OK, recovered.StatusCode);
    }
}
