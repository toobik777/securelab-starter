using Microsoft.AspNetCore.Antiforgery;

namespace SecureLab.Api.Security;

/// <summary>
/// ЛР 6: стандартний antiforgery flow ASP.NET Core для cookie-authenticated mutating endpoint-ів.
/// Контракт із клієнтом: request token видається маршрутом <see cref="TokenRoute"/> після login і повертається
/// в заголовку <see cref="HeaderName"/>; antiforgery cookie (HttpOnly, SameSite=Strict) надсилає браузер.
/// </summary>
public static class AntiforgeryContract
{
    public const string HeaderName = "X-CSRF-TOKEN";
    public const string TokenRoute = "/api/auth/csrf-token";

    public static void AddLab06Antiforgery(this WebApplicationBuilder builder)
    {
        builder.Services.AddAntiforgery(options =>
        {
            options.HeaderName = HeaderName;
            options.Cookie.Name = "SecureLab.Antiforgery";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Strict;
            // Той самий profile, що й authentication cookie ЛР 3: Secure поза Development.
            options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
                ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
            // Заборона вбудовування задає CSP frame-ancestors 'none' та X-Frame-Options: DENY у спільному middleware.
            options.SuppressXFrameOptionsHeader = true;
        });
    }

    /// <summary>Маршрут видачі токена: лише для автентифікованого користувача, токен пов'язано з його identity.</summary>
    public static void MapLab06Antiforgery(this WebApplication app)
    {
        app.MapGet(TokenRoute, (HttpContext context, IAntiforgery antiforgery) =>
        {
            var tokens = antiforgery.GetAndStoreTokens(context);
            return Results.Ok(new CsrfTokenResponse(tokens.RequestToken!));
        }).RequireAuthorization().WithName("GetCsrfToken");
    }

    /// <summary>Єдине місце валідації для всіх mutating маршрутів периметра (ЛР 6, таблиця SC-xx).</summary>
    public static TBuilder RequireAntiforgery<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder =>
        builder.AddEndpointFilter<TBuilder, AntiforgeryValidationFilter>();
}

public sealed record CsrfTokenResponse(string Token);

public sealed class AntiforgeryValidationFilter(IAntiforgery antiforgery) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        try
        {
            await antiforgery.ValidateRequestAsync(context.HttpContext);
        }
        catch (AntiforgeryValidationException)
        {
            // Один точний 4xx у форматі Problem Details; handler не виконується, стан не змінюється.
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Antiforgery token is missing or invalid.");
        }

        return await next(context);
    }
}
