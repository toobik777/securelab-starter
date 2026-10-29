using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace SecureLab.Api.Scaffolding;

/// <summary>Налаштування обмеження спроб; значення читаються з конфігурації (Lab03:Login, Lab03:Register).</summary>
public class AttemptLimitOptions
{
    public int PermitLimit { get; set; } = 5;
    public int WindowSeconds { get; set; } = 60;
}

public sealed class LoginLimitOptions : AttemptLimitOptions;

public sealed class RegisterLimitOptions : AttemptLimitOptions
{
    public RegisterLimitOptions() => PermitLimit = 10;
}

public static class Lab03Security
{
    public const string LoginPolicy = "lab03-login";
    public const string RegisterPolicy = "lab03-register";

    public static void AddLab03RateLimiting(this WebApplicationBuilder builder)
    {
        builder.Services.Configure<LoginLimitOptions>(builder.Configuration.GetSection("Lab03:Login"));
        builder.Services.Configure<RegisterLimitOptions>(builder.Configuration.GetSection("Lab03:Register"));

        builder.Services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            limiter.AddPolicy(LoginPolicy, context =>
            {
                var options = context.RequestServices.GetRequiredService<IOptions<LoginLimitOptions>>().Value;
                return FixedWindow(context, "login", options);
            });
            limiter.AddPolicy(RegisterPolicy, context =>
            {
                var options = context.RequestServices.GetRequiredService<IOptions<RegisterLimitOptions>>().Value;
                return FixedWindow(context, "register", options);
            });
            limiter.OnRejected = async (rejected, cancellationToken) =>
            {
                var context = rejected.HttpContext;
                if (rejected.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.Response.Headers.RetryAfter =
                        ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(System.Globalization.CultureInfo.InvariantCulture);
                }
                var log = context.RequestServices.GetRequiredService<ILogger<AttemptLimitOptions>>();
                AuthEvents.Throttled(log, context.Request.Path.Value ?? string.Empty,
                    context.Connection.RemoteIpAddress?.ToString() ?? "unknown");
                context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                await context.Response.WriteAsJsonAsync(new
                {
                    type = "https://tools.ietf.org/html/rfc6585#section-4",
                    title = "Too Many Requests",
                    status = 429
                }, cancellationToken);
            };
        });
    }

    // Ключ розділу: лише адреса клієнта. Ім'я користувача з body на цьому етапі ще не прочитане,
    // а ключ за іменем дозволяв би навмисно блокувати чужий акаунт (denial of service).
    private static RateLimitPartition<string> FixedWindow(HttpContext context, string scope, AttemptLimitOptions options)
    {
        var key = scope + ":" + (context.Connection.RemoteIpAddress?.ToString() ?? "unknown");
        return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = options.PermitLimit,
            Window = TimeSpan.FromSeconds(options.WindowSeconds),
            QueueLimit = 0,
            AutoReplenishment = true
        });
    }
}

/// <summary>
/// Мінімальні події автентифікації. Навмисно виключено: password, hash, cookie, token,
/// заголовок Authorization, тіло запиту і введене ім'я користувача при невдалому вході.
/// </summary>
public static partial class AuthEvents
{
    [LoggerMessage(EventId = 3001, Level = LogLevel.Information,
        Message = "Auth event: register succeeded, userId={UserId}")]
    public static partial void RegisterSucceeded(ILogger logger, Guid userId);

    [LoggerMessage(EventId = 3002, Level = LogLevel.Information,
        Message = "Auth event: register rejected, reason={Reason}")]
    public static partial void RegisterRejected(ILogger logger, string reason);

    [LoggerMessage(EventId = 3003, Level = LogLevel.Information,
        Message = "Auth event: login succeeded, userId={UserId}, remoteIp={RemoteIp}")]
    public static partial void LoginSucceeded(ILogger logger, Guid userId, string remoteIp);

    [LoggerMessage(EventId = 3004, Level = LogLevel.Warning,
        Message = "Auth event: login failed, remoteIp={RemoteIp}")]
    public static partial void LoginFailed(ILogger logger, string remoteIp);

    [LoggerMessage(EventId = 3005, Level = LogLevel.Information,
        Message = "Auth event: logout, userId={UserId}")]
    public static partial void Logout(ILogger logger, Guid userId);

    [LoggerMessage(EventId = 3006, Level = LogLevel.Warning,
        Message = "Auth event: rate limit exceeded, path={Path}, remoteIp={RemoteIp}")]
    public static partial void Throttled(ILogger logger, string path, string remoteIp);
}
