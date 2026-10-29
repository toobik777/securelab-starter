using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Logging;

namespace SecureLab.Api.Tests;

public sealed class SecureLabApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        // Звичайні тести не мають впиратися в обмеження входів; окремі сценарії 429 задають малі ліміти.
        builder.UseSetting("Lab03:Login:PermitLimit", "10000");
        builder.UseSetting("Lab03:Register:PermitLimit", "10000");
        builder.ConfigureLogging(logging => logging.AddProvider(CapturedLogs.Provider));
    }
}

/// <summary>Збирає повідомлення журналу застосунку, щоб довести відсутність credentials у подіях.</summary>
public static class CapturedLogs
{
    private static readonly List<string> Messages = [];
    public static readonly ILoggerProvider Provider = new CapturingProvider();

    public static string[] Snapshot()
    {
        lock (Messages) return Messages.ToArray();
    }

    private sealed class CapturingProvider : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new CapturingLogger();
        public void Dispose() { }
    }

    private sealed class CapturingLogger : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var text = formatter(state, exception);
            lock (Messages) Messages.Add(text);
        }
    }
}
