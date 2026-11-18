using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SecureLab.Api.Data;

namespace SecureLab.Api.Tests;

// Захист ЛР 3: test host із handler, який повертає Success, але identity без authenticationType.
public sealed class DefenseHandlerTests(SecureLabApiFactory factory) : IClassFixture<SecureLabApiFactory>
{
    private sealed class TestHandler(IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, DbSeeder.BobId.ToString()),
                new Claim(ClaimTypes.Name, "bob"),
                new Claim(ClaimTypes.Role, "Reporter")
            };

            
            var identity = new ClaimsIdentity(claims, Scheme.Name);              
           

            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }

    // Test-only маршрут: показує, що handler фактично поклав у HttpContext.User. Лише в test host.
    private sealed class WhoAmIStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.UseRouting();
            app.UseAuthentication();
            app.UseEndpoints(endpoints => endpoints.MapGet("/test-only/whoami",
                (HttpContext context) => Results.Ok(context.User.Identity?.IsAuthenticated ?? false)));
            next(app);
        };
    }

    // Identity задає власні Default*-схеми, тому для test host перекриваємо всі три.
    private WebApplicationFactory<Program> TestHost() => factory.WithWebHostBuilder(builder =>
        builder.ConfigureTestServices(services =>
        {
            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = "Test";
                options.DefaultChallengeScheme = "Test";
                options.DefaultForbidScheme = "Test";
            }).AddScheme<AuthenticationSchemeOptions, TestHandler>("Test", _ => { });
            services.AddTransient<IStartupFilter, WhoAmIStartupFilter>();
        }));

    [Fact]
    public async Task TestHost_HandlerIdentity_IsAuthenticated()
    {
        using var host = TestHost();
        using var client = host.CreateClient();

        var isAuthenticated = await client.GetFromJsonAsync<bool>("/test-only/whoami");

        Assert.True(isAuthenticated, "Identity без authenticationType: IsAuthenticated має бути false до правки.");
    }

    [Fact]
    public async Task TestHost_Me_WithHandlerIdentity_ReturnsBob()
    {
        using var host = TestHost();
        using var client = host.CreateClient();

        using var response = await client.GetAsync("/api/me");

        // До правки: 403 (handler Success, але policy не пройдено). Після правки: 200 і Bob.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(DbSeeder.BobId.ToString(), await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task NormalHost_SameTestHeader_WithoutCookie_Returns401()
    {
        using var client = factory.Anonymous();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/me");
        request.Headers.Add("X-Test-User", "bob");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
