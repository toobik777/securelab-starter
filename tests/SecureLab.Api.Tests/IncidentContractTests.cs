using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SecureLab.Api.Data;
using Xunit;

namespace SecureLab.Api.Tests;

public class IncidentContractTests : IClassFixture<SecureLabApiFactory>
{
    private readonly SecureLabApiFactory _factory;

    public IncidentContractTests(SecureLabApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Create_Incident_InvalidSeverity_ReturnsBadRequestWithValidationProblem()
    {
        var client = _factory.CreateClient();

        var requestBody = new
        {
            title = "Test Severity Validation",
            description = "Це тестовий опис інциденту, який містить більше ніж сорок символів для проходження перевірки.",
            severity = "7", // Невалідне значення
            occurredAtUtc = DateTimeOffset.UtcNow
        };

        var response = await client.PostAsJsonAsync("/api/incidents", requestBody);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        // Читаємо відповідь як JSON для перевірки наявності помилок валідації
        var contentString = await response.Content.ReadAsStringAsync();
        using var jsonDoc = JsonDocument.Parse(contentString);
        var root = jsonDoc.RootElement;

        // Перевіряємо наявність поля errors та ключа Severity
        Assert.True(root.TryGetProperty("errors", out var errorsElement));
        Assert.True(errorsElement.TryGetProperty("Severity", out _));
    }

    [Fact]
    public async Task Create_DuplicateIncident_FirstCreates_SecondReturnsConflict()
    {
        var client = _factory.CreateClient();
        var uniqueTitle = $"Regression-{Guid.NewGuid():N}";

        var requestBody = new
        {
            title = uniqueTitle,
            description = "Це достатньо довгий опис для інциденту, який успішно пройде валідацію за довжиною символів.",
            severity = "High",
            occurredAtUtc = DateTimeOffset.UtcNow
        };

        try
        {
            // 1. Перший запит — успішне створення (201 Created)
            var response1 = await client.PostAsJsonAsync("/api/incidents", requestBody);
            Assert.Equal(HttpStatusCode.Created, response1.StatusCode);

            // 2. Другий запит з тим самим заголовком — конфлікт (409 Conflict)
            var response2 = await client.PostAsJsonAsync("/api/incidents", requestBody);
            Assert.Equal(HttpStatusCode.Conflict, response2.StatusCode);
            Assert.Equal("application/problem+json", response2.Content.Headers.ContentType?.MediaType);
        }
        finally
        {
            // Очищення тестових даних через БД
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SecureLabDbContext>();
            await db.Incidents
                .Where(i => i.Title == uniqueTitle)
                .ExecuteDeleteAsync();
        }
    }
}