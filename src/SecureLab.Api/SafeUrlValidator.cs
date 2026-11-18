namespace SecureLab.Api.Services;

/// <summary>
/// Перевіряє URL, який надійшов від користувача: дозволяє лише відносні шляхи
/// або абсолютні адреси того ж origin (http://localhost:5080) без userinfo.
/// </summary>
public static class SafeUrlValidator
{
    private static readonly Uri BaseUri = new("http://localhost:5080");

    /// <returns>Нормалізована абсолютна адреса або <c>null</c>, якщо URL небезпечний чи некоректний.</returns>
    public static string? ValidateAndParseUrl(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        Uri? uri;
        if (input.StartsWith('/') && !input.StartsWith("//", StringComparison.Ordinal))
        {
            // Відносний шлях розв'язується відносно власного origin.
            if (!Uri.TryCreate(BaseUri, input, out uri))
            {
                return null;
            }
        }
        else if (!Uri.TryCreate(input, UriKind.Absolute, out uri))
        {
            return null;
        }

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            return null;
        }

        // userinfo (user:pass@host) використовують для маскування справжнього хоста.
        if (!string.IsNullOrEmpty(uri.UserInfo) || input.Contains('@') && uri.Authority != BaseUri.Authority)
        {
            return null;
        }

        if (!uri.Host.Equals(BaseUri.Host, StringComparison.OrdinalIgnoreCase) || uri.Port != BaseUri.Port)
        {
            return null;
        }

        return uri.AbsoluteUri;
    }
}
