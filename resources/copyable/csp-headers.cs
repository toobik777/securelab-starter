// Змініть наявну middleware заголовків перед статичними файлами.
var csp = "default-src 'self'; frame-ancestors 'none'";

app.Use(async (context, next) =>
{
    context.Response.Headers.XContentTypeOptions = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";

    var isDevelopmentApiReference =
        app.Environment.IsDevelopment()
        && context.Request.Path.StartsWithSegments("/scalar");

    if (!isDevelopmentApiReference)
    {
        context.Response.Headers[
            "Content-Security-Policy-Report-Only"] = csp;
    }

    await next(context);
});
