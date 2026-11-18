using SecureLab.Api.Services;
using Xunit;

namespace SecureLab.Api.Tests;

public class SafeUrlValidatorTests
{
    [Theory]
    [InlineData("/health", "http://localhost:5080/health")]
    [InlineData("http;//demo@localhost:5080/health", null)]
    [InlineData("http://localhost:5080@localhost:4174/health", null)]
    [InlineData("http://[bad", null)]
    [InlineData("http://:demo@localhost:5080/health", null)]
    [InlineData("http://localhost:5080/#@demo", "http://localhost:5080/#@demo")]
    [InlineData("/health?user=demo", "http://localhost:5080/health?user=demo")]
    public void ValidateAndParseUrl_ShouldReturnExpectedResult(string input, string? expected)
    {
        string? result = SafeUrlValidator.ValidateAndParseUrl(input);
        Assert.Equal(expected, result);
    }
}
