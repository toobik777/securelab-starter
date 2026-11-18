using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace SecureLab.Api.Tests;

/// <summary>ЛР 6: повторні перевірки контролів ЛР 2 і ЛР 5 у фактичному fixed state (regression pack).</summary>
public sealed class RegressionPackTests(SecureLabApiFactory factory) : IClassFixture<SecureLabApiFactory>
{
    // REG-SQLI-01 (повторює S-02 ЛР 2): query не змінює структуру SQL.
    [Fact]
    public async Task RegSqli01_SearchInjectionPayloads_DoNotChangeQueryStructure_AndNormalSearchStillWorks()
    {
        using var alice = await factory.LoginAsync("alice", TestPasswords.Seed);

        foreach (var payload in new[] { "' OR '1'='1", "'; DROP TABLE incidents; --", "%", "_", "\\" })
        {
            using var response = await alice.GetAsync($"/api/incidents/search?q={Uri.EscapeDataString(payload)}");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var hits = await response.Content.ReadFromJsonAsync<List<JsonElement>>();
            Assert.Empty(hits!); // payload — літерал для ILIKE, а не фрагмент SQL; wildcard екрановано
        }

        // allowlist сортування: довільний вираз у sortBy відхиляється.
        using var badSort = await alice.GetAsync("/api/incidents/search?q=a&sortBy=title%3BDROP%20TABLE%20incidents");
        Assert.Equal(HttpStatusCode.BadRequest, badSort.StatusCode);

        // Позитивна регресія: звичайний пошук і сортування працюють, таблиця на місці.
        var normal = await alice.GetFromJsonAsync<List<JsonElement>>("/api/incidents/search?q=%D0%BD%D0%B0%D0%B2%D1%87&sortBy=severity");
        Assert.NotEmpty(normal!);
    }

    // REG-CORS-01 (ЛР 5, автоматизовано у ЛР 6): точний origin, без wildcard і без відбиття Origin. Development-профіль.
    [Fact]
    public async Task RegCors01_OnlyExactDevelopmentOriginIsAllowed()
    {
        using var client = factory.Anonymous("http://localhost");

        async Task<HttpResponseMessage> WithOrigin(string origin)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/health");
            request.Headers.Add("Origin", origin);
            return await client.SendAsync(request);
        }

        using var allowed = await WithOrigin("http://localhost:4173");
        Assert.Equal("http://localhost:4173", allowed.Headers.GetValues("Access-Control-Allow-Origin").Single());
        Assert.Equal("true", allowed.Headers.GetValues("Access-Control-Allow-Credentials").Single());

        foreach (var origin in new[] { "http://localhost:4174", "https://evil.example" })
        {
            using var denied = await WithOrigin(origin);
            Assert.Equal(HttpStatusCode.OK, denied.StatusCode);          // запит виконується, але браузер не віддасть відповідь JS
            Assert.False(denied.Headers.Contains("Access-Control-Allow-Origin"));
        }

        // Preflight недозволеного origin не отримує allow-заголовків.
        using var preflight = new HttpRequestMessage(HttpMethod.Options, "/api/incidents");
        preflight.Headers.Add("Origin", "http://localhost:4174");
        preflight.Headers.Add("Access-Control-Request-Method", "GET");
        using var preflightResponse = await client.SendAsync(preflight);
        Assert.False(preflightResponse.Headers.Contains("Access-Control-Allow-Origin"));
    }

    // Validation (T-02 ЛР 2 у cookie-периметрі ЛР 6): токен є, тож перевірка DTO справді виконується.
    [Fact]
    public async Task Validation_WithValidToken_ReturnsErrorsField_AndValidInputIsStored()
    {
        using var alice = await factory.LoginWithTokenAsync("alice", TestPasswords.Seed);

        using var invalid = await alice.PostAsJsonAsync("/api/incidents",
            new { title = "", description = "", severity = "7", occurredAtUtc = DateTimeOffset.UtcNow });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        using var problem = JsonDocument.Parse(await invalid.Content.ReadAsStringAsync());
        Assert.True(problem.RootElement.GetProperty("errors").TryGetProperty("Severity", out _));

        var title = "REG-VALID-" + Guid.NewGuid().ToString("N");
        using var valid = await alice.PostAsJsonAsync("/api/incidents", new
        {
            title,
            description = "Допустимий опис довжиною понад сорок символів для перевірки збереження.",
            severity = "Low",
            occurredAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1)
        });
        Assert.Equal(HttpStatusCode.Created, valid.StatusCode);
        using var created = JsonDocument.Parse(await valid.Content.ReadAsStringAsync());
        var id = created.RootElement.GetProperty("id").GetGuid();
        using var delete = await alice.DeleteAsync($"/api/incidents/{id}");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
    }
}
