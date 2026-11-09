using SecureLab.Api.Scaffolding;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;
using SecureLab.Api.Application.Incidents;
using SecureLab.Api.Data;
using SecureLab.Api.Presentation.Endpoints;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    WebRootPath = "Client"
});
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();

var connectionString = builder.Configuration.GetConnectionString("SecureLab")
    ?? throw new InvalidOperationException(
        "Connection string 'SecureLab' не налаштовано.");

builder.Services.AddDbContext<SecureLabDbContext>(options =>
    options.UseNpgsql(connectionString));
builder.Services.AddScoped<IncidentQueries>();
builder.Services.AddScoped<Microsoft.AspNetCore.Authorization.IAuthorizationHandler, IncidentAuthorizationHandler>();

builder.AddLab03Identity();
builder.AddLab03RateLimiting();

// ЛР 5: навчальна CORS policy лише для Development. Штатний клієнт і API мають один origin (http://localhost:5080),
// тому CORS у цій топології не потрібен. Дозволено ТІЛЬКИ probe http://localhost:4173 (без AllowAnyOrigin,
// без відбиття Origin); http://localhost:4174 лишається негативним контролем. У Production policy не реєструється.
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddCors(options =>
    {
        options.AddPolicy("Lab05BrowserProbe", policy =>
        {
            policy.WithOrigins("http://localhost:4173")
                  .WithMethods("GET")
                  .WithHeaders("X-Lab-Cors-Probe")
                  .AllowCredentials();
        });
    });
}

var app = builder.Build();

var resetRequested = args.Contains("--reset-database", StringComparer.Ordinal);
await DatabaseBootstrap.InitializeAsync(app.Services, app.Configuration, app.Environment, resetRequested);

if (resetRequested)
{
    return;
}

app.UseExceptionHandler();
app.UseStatusCodePages();

// ЛР 5: CSP як defense in depth (НЕ замінює safe DOM rendering). Політику складено з фактичних ресурсів клієнта:
// власні /app.js, /auth-lab.js, /browser-lab.js, власні CSS, same-origin fetch до API; inline-скриптів і inline-стилів немає
// (inline <style> сторінки auth-lab.html винесено в auth-lab.css), тому 'unsafe-inline' не потрібен.
const string contentSecurityPolicy =
    "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self'; connect-src 'self'; "
    + "object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'";
const string cspHeaderName = "Content-Security-Policy-Report-Only";

app.Use(async (context, next) =>
{
    context.Response.Headers.XContentTypeOptions = "nosniff";
    context.Response.Headers.Append("Referrer-Policy", "no-referrer");
    // Development-довідник /scalar використовує сторонній UI-пакет з власними ресурсами: виняток лише тут і лише в Development.
    var isDevelopmentApiReference =
        app.Environment.IsDevelopment() && context.Request.Path.StartsWithSegments("/scalar");
    if (!isDevelopmentApiReference)
    {
        context.Response.Headers[cspHeaderName] = contentSecurityPolicy;
    }
    await next(context);
});

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

// CORS стоїть після routing і до rate limiter/authentication/authorization: дозволені headers
// додаються також до 401/403/429, а preflight обробляється до перевірок доступу.
app.UseRouting();
if (app.Environment.IsDevelopment())
{
    app.UseCors("Lab05BrowserProbe");
}

app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/health", async (SecureLabDbContext dbContext, CancellationToken cancellationToken) =>
    await dbContext.Database.CanConnectAsync(cancellationToken)
        ? Results.Ok(new { status = "ready" })
        : Results.Problem(
            title: "База даних недоступна",
            statusCode: StatusCodes.Status503ServiceUnavailable))
    .WithName("GetHealth")
    .Produces(StatusCodes.Status200OK)
    .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

app.MapIncidentEndpoints();
app.MapLab03Identity();
app.MapLab02Endpoints();
app.MapFallbackToFile("index.html");

app.Run();

public partial class Program;
