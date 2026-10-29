using System.Net.Mail;
using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using SecureLab.Api.Data;
using SecureLab.Api.Data.Entities;

namespace SecureLab.Api.Scaffolding;

public static class Lab03Identity
{
    public static void AddLab03Identity(this WebApplicationBuilder builder)
    {
        builder.Services.AddIdentity<ApplicationUser, IdentityRole<Guid>>(options =>
        {
            options.User.RequireUniqueEmail = true;
            options.Password.RequiredLength = 10;
        }).AddEntityFrameworkStores<SecureLabDbContext>().AddDefaultTokenProviders();
        builder.Services.AddAuthorization();
        builder.Services.ConfigureApplicationCookie(options =>
        {
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
                ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
            options.Events.OnRedirectToLogin = context =>
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            };
            options.Events.OnRedirectToAccessDenied = context =>
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            };
        });
        builder.Services.AddSingleton<LoginTimingGuard>();
    }

    public static void MapLab03Identity(this WebApplication app)
    {
        app.MapPost("/api/auth/register", RegisterAsync)
            .RequireRateLimiting(Lab03Security.RegisterPolicy);
        app.MapPost("/api/auth/login", LoginAsync)
            .RequireRateLimiting(Lab03Security.LoginPolicy);
        app.MapPost("/api/auth/logout", LogoutAsync);
        app.MapGet("/api/me", GetCurrentUser).RequireAuthorization();
        // Зразок рядка, що одночасно реєструє endpoint і вимагає автентифікацію.
        app.MapGet("/api/auth/session-check", () => Results.NoContent()).RequireAuthorization();
    }

    // Межі контракту: UserName/Email у БД до 256, DisplayName до 120 (SecureLabDbContext).
    private const int UserNameMin = 3, UserNameMax = 64, EmailMax = 256, DisplayNameMax = 120, PasswordMax = 128;

    private static async Task<IResult> RegisterAsync(
        RegisterRequest input, UserManager<ApplicationUser> users, ILogger<ApplicationUser> log)
    {
        var userName = input.UserName?.Trim() ?? string.Empty;
        var email = input.Email?.Trim() ?? string.Empty;
        var displayName = input.DisplayName?.Trim() ?? string.Empty;
        var password = input.Password ?? string.Empty; // password не нормалізується

        var errors = new Dictionary<string, string[]>();
        if (userName.Length < UserNameMin || userName.Length > UserNameMax)
            errors["UserName"] = [$"UserName must be {UserNameMin}-{UserNameMax} characters."];
        if (email.Length == 0 || email.Length > EmailMax || !IsPlainEmail(email))
            errors["Email"] = ["Email is required and must be a valid address."];
        if (displayName.Length == 0 || displayName.Length > DisplayNameMax)
            errors["DisplayName"] = [$"DisplayName is required and must not exceed {DisplayNameMax} characters."];
        if (password.Length == 0 || password.Length > PasswordMax)
            errors["Password"] = [$"Password is required and must not exceed {PasswordMax} characters."];
        if (errors.Count > 0)
        {
            AuthEvents.RegisterRejected(log, "validation");
            return Results.ValidationProblem(errors);
        }

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(), UserName = userName, Email = email, DisplayName = displayName
        };
        var result = await users.CreateAsync(user, password);
        if (!result.Succeeded)
        {
            var codes = result.Errors.Select(error => error.Code).ToHashSet();
            var invalid = new Dictionary<string, string[]>();
            if (codes.Any(code => code.StartsWith("Password", StringComparison.Ordinal)))
                invalid["Password"] = ["Password does not meet the policy (min 10 chars, upper, lower, digit, symbol)."];
            if (codes.Contains("InvalidUserName"))
                invalid["UserName"] = ["UserName may contain only latin letters, digits and -._@+ ."];
            if (codes.Contains("InvalidEmail"))
                invalid["Email"] = ["Email is not valid."];
            if (invalid.Count > 0)
            {
                AuthEvents.RegisterRejected(log, "policy");
                return Results.ValidationProblem(invalid);
            }
            if (codes.Contains("DuplicateUserName") || codes.Contains("DuplicateEmail"))
            {
                // Послідовний контракт: 409 без уточнення, що саме зайнято (name чи email).
                AuthEvents.RegisterRejected(log, "duplicate");
                return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Conflict",
                    detail: "An account with these details already exists.");
            }
            AuthEvents.RegisterRejected(log, "other");
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Registration failed");
        }

        var role = await users.AddToRoleAsync(user, DbSeeder.ReporterRole);
        if (!role.Succeeded) throw new InvalidOperationException("Default role assignment failed.");
        AuthEvents.RegisterSucceeded(log, user.Id);
        return Results.Created("/api/me", new RegisterResponse(user.Id, user.UserName!, user.DisplayName));
    }

    private static async Task<IResult> LoginAsync(
        LoginRequest input, HttpContext context, UserManager<ApplicationUser> users,
        SignInManager<ApplicationUser> signIn, LoginTimingGuard timing, ILogger<ApplicationUser> log)
    {
        var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        if (string.IsNullOrWhiteSpace(input.UserName) || string.IsNullOrEmpty(input.Password))
        {
            AuthEvents.LoginFailed(log, ip);
            return Results.Unauthorized();
        }

        var user = await users.FindByNameAsync(input.UserName);
        if (user is null)
        {
            // Вирівнювання часу: невідомий користувач теж платить вартість перевірки hash,
            // інакше різниця у затримці розкриває існування акаунта.
            timing.SpendVerificationCost(input.Password);
            AuthEvents.LoginFailed(log, ip);
            return Results.Unauthorized();
        }

        var result = await signIn.PasswordSignInAsync(user, input.Password, false, false);
        if (!result.Succeeded)
        {
            AuthEvents.LoginFailed(log, ip);
            return Results.Unauthorized();
        }

        AuthEvents.LoginSucceeded(log, user.Id, ip);
        return Results.NoContent();
    }

    private static async Task<IResult> LogoutAsync(
        HttpContext context, SignInManager<ApplicationUser> signIn, ILogger<ApplicationUser> log)
    {
        var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        await signIn.SignOutAsync();
        AuthEvents.Logout(log, Guid.TryParse(userId, out var id) ? id : Guid.Empty);
        return Results.NoContent();
    }

    // Лише verified principal: id і явно дозволені claims беруться з HttpContext.User.
    private static IResult GetCurrentUser(HttpContext httpContext)
    {
        var userId = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!Guid.TryParse(userId, out var verifiedUserId))
        {
            return Results.Problem(statusCode: StatusCodes.Status500InternalServerError);
        }
        var userName = httpContext.User.FindFirst(ClaimTypes.Name)?.Value ?? string.Empty;
        var roles = httpContext.User.FindAll(ClaimTypes.Role).Select(claim => claim.Value).ToArray();
        return Results.Ok(new CurrentUserResponse(verifiedUserId, userName, roles));
    }

    private static bool IsPlainEmail(string value) =>
        MailAddress.TryCreate(value, out var address) && address.Address == value;
}

// Request DTO: лише поля, які клієнт має право передати. Id, роль і hash сюди не входять.
public sealed record RegisterRequest(string? UserName, string? Email, string? DisplayName, string? Password);
public sealed record LoginRequest(string? UserName, string? Password);

// Response DTO: явно обрані безпечні поля.
public sealed record RegisterResponse(Guid Id, string UserName, string DisplayName);
public sealed record CurrentUserResponse(Guid UserId, string UserName, string[] Roles);

/// <summary>
/// Сервіс вирівнювання часу login для невідомого імені користувача. Використовує стандартний
/// PasswordHasher із тими самими параметрами, що й Identity, а не власну криптографію.
/// </summary>
public class LoginTimingGuard
{
    private readonly PasswordHasher<ApplicationUser> _hasher;
    private readonly ApplicationUser _dummyUser = new() { DisplayName = "dummy", UserName = "dummy" };
    private readonly string _dummyHash;

    public LoginTimingGuard(Microsoft.Extensions.Options.IOptions<PasswordHasherOptions> options)
    {
        _hasher = new PasswordHasher<ApplicationUser>(options);
        _dummyHash = _hasher.HashPassword(_dummyUser, Guid.NewGuid().ToString("N") + "-Aa1");
    }

    public virtual void SpendVerificationCost(string password) =>
        _hasher.VerifyHashedPassword(_dummyUser, _dummyHash, password);
}
