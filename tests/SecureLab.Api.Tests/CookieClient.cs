using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace SecureLab.Api.Tests;

public static class CookieClient
{
    public const string TokenRoute = "/api/auth/csrf-token";
    public const string TokenHeader = "X-CSRF-TOKEN";

    // ЛР 6: тип параметра — WebApplicationFactory<Program>, щоб той самий helper працював і зі звичайним
    // SecureLabApiFactory, і з похідним production-host (WithWebHostBuilder). Наявні виклики не змінюються.
    public static async Task<HttpClient> LoginAsync(this WebApplicationFactory<Program> factory, string userName, string password)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false, HandleCookies = true
        });
        using var response = await client.PostAsJsonAsync("/api/auth/login", new { userName, password });
        if (!response.IsSuccessStatusCode)
        {
            client.Dispose();
            throw new InvalidOperationException("Study login failed (credentials omitted).");
        }
        return client;
    }

    /// <summary>Login + видача antiforgery request token тим самим cookie-сеансом (токен — у DefaultRequestHeaders клієнта).</summary>
    public static async Task<HttpClient> LoginWithTokenAsync(this WebApplicationFactory<Program> factory, string userName, string password)
    {
        var client = await factory.LoginAsync(userName, password);
        await client.UseAntiforgeryTokenAsync();
        return client;
    }

    /// <summary>
    /// Одержує токен і ставить header для цього клієнта. Лише same-origin адреси власного API:
    /// DefaultRequestHeaders надсилається з кожним наступним запитом. Після logout/new login — викликати знову.
    /// </summary>
    public static async Task UseAntiforgeryTokenAsync(this HttpClient client, string tokenRoute = TokenRoute, string headerName = TokenHeader)
    {
        client.DefaultRequestHeaders.Remove(headerName);
        using var response = await client.GetAsync(tokenRoute);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<TokenResponse>();
        if (string.IsNullOrWhiteSpace(body?.Token))
        {
            throw new InvalidOperationException("Missing request token.");
        }
        client.DefaultRequestHeaders.Add(headerName, body.Token);
    }

    private sealed record TokenResponse(string Token);
}
