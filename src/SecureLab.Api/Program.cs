using SecureLab.Api.Scaffolding;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;
using SecureLab.Api.Application.Incidents;
using SecureLab.Api.Data;
using SecureLab.Api.Presentation.Endpoints;
using SecureLab.Api.Security;

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
builder.AddLab06Antiforgery();

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

// ЛР 6 (F-01): заголовки безпеки виставляються в OnStarting у найзовнішньому middleware, ДО UseExceptionHandler.
// ExceptionHandlerMiddleware очищає заголовки відповіді перед записом 500; callback, зареєстрований раніше,
// спрацьовує під час фактичного початку відповіді, тому заголовки є й на error path.
// CSP (ЛР 5) — defense in depth, НЕ замінює safe DOM rendering. Політику складено з фактичних ресурсів клієнта:
// власні /app.js, /auth-lab.js, /browser-lab.js, /csrf.js, власні CSS, same-origin fetch; inline-скриптів і стилів немає.
const string contentSecurityPolicy =
    "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self'; connect-src 'self'; "
    + "object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'";

app.Use((context, next) =>
{
    context.Response.OnStarting(state =>
    {
        var httpContext = (HttpContext)state;
        var headers = httpContext.Response.Headers;
        headers.XContentTypeOptions = "nosniff";
        headers["Referrer-Policy"] = "no-referrer";
        headers["X-Frame-Options"] = "DENY";
        // Development-довідник /scalar використовує сторонній UI-пакет з власними ресурсами: виняток лише тут і лише в Development.
        var isDevelopmentApiReference =
            httpContext.RequestServices.GetRequiredService<IHostEnvironment>().IsDevelopment()
            && httpContext.Request.Path.StartsWithSegments("/scalar");
        if (!isDevelopmentApiReference)
        {
            headers["Content-Security-Policy"] = contentSecurityPolicy;
        }

        // ЛР 6 (F-03): відповіді API містять дані автентифікованого користувача — не кешувати (browser/shared cache).
        if (httpContext.Request.Path.StartsWithSegments("/api"))
        {
            headers.CacheControl = "no-store";
        }

        return Task.CompletedTask;
    }, context);
    return next(context);
});

app.UseExceptionHandler();
app.UseStatusCodePages();

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

// ЛР 6: limiter стоїть після authentication, щоб search-policy розділяла partitions за автентифікованим користувачем;
// login-policy далі розділяє за адресою клієнта (body ще не прочитано).
app.UseAuthentication();
app.UseRateLimiter();
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
app.MapLab06Antiforgery();
// ЛР 6 (F-03): невідомий API-шлях — це 404 Problem Details, а не SPA-fallback (200 text/html).
app.MapFallback("/api/{**path}", () => Results.Problem(
    title: "Ресурс не знайдено", statusCode: StatusCodes.Status404NotFound));
app.MapFallbackToFile("index.html");

app.Run();

public partial class Program;
