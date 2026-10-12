using Microsoft.EntityFrameworkCore;
using SecureLab.Api.Data;
using SecureLab.Api.Data.Entities;

namespace SecureLab.Api.Scaffolding;

// Навчальний старт ЛР 02. Запускати лише з локальними штучними даними.
public static class Lab02Endpoints
{
    public static void MapLab02Endpoints(this WebApplication app)
    {
        app.MapGet("/api/incidents/search", async (string? q, string? sortBy, SecureLabDbContext db, CancellationToken ct) =>
        {
            var query = db.Incidents.AsNoTracking();

            // 1. Безпечна обробка пошукового запиту через EscapeLike та ILike
            if (!string.IsNullOrEmpty(q))
            {
                var escaped = EscapeLike(q);
                var pattern = $"%{escaped}%";
                query = query.Where(i => EF.Functions.ILike(i.Title, pattern, "\\") || EF.Functions.ILike(i.Description, pattern, "\\"));
            }

            // 2. Безпечне сортування за білим списком (Allowlist) із числовими рангами та ThenBy(Id)
            query = sortBy switch
            {
                null or "" or "createdAtUtc" => query
                    .OrderByDescending(i => i.CreatedAtUtc)
                    .ThenBy(i => i.Id),

                "severity" => query
                    .OrderBy(i => i.Severity == IncidentSeverity.Critical ? 0 :
                                  i.Severity == IncidentSeverity.High ? 1 :
                                  i.Severity == IncidentSeverity.Medium ? 2 : 3)
                    .ThenBy(i => i.Id),

                "status" => query
                    .OrderBy(i => i.Status == IncidentStatus.New ? 0 :
                                  i.Status == IncidentStatus.Triaged ? 1 :
                                  i.Status == IncidentStatus.InProgress ? 2 :
                                  i.Status == IncidentStatus.Resolved ? 3 : 4)
                    .ThenBy(i => i.Id),

                _ => throw new BadHttpRequestException("Invalid sortBy parameter.", statusCode: StatusCodes.Status400BadRequest)
            };

            // 3. Обмеження вибірки (Take 50) після сортування та матеріалізація
            var rows = await query.Take(50).ToListAsync(ct);

            // 4. Проєкція у фіксований Response DTO (IncidentSearchItemResponse)
            var response = rows.Select(row => new IncidentSearchItemResponse(
                row.Id,
                row.Title,
                row.Description,
                row.Severity.ToString(),
                row.Status.ToString(),
                row.CreatedAtUtc
            ));

            return Results.Ok(response);
        });

        app.MapPost("/api/incidents", async (CreateIncidentRequest request, SecureLabDbContext db, CancellationToken ct) =>
        {
            var now = DateTimeOffset.UtcNow;
            var errors = new Dictionary<string, string[]>();

            // 3. Валідація Title
            var rawTitle = request.Title ?? string.Empty;
            if (rawTitle.Length > 160)
            {
                errors["Title"] = new[] { "Title must not exceed 160 characters." };
            }
            var trimmedTitle = rawTitle.Trim();
            if (string.IsNullOrWhiteSpace(trimmedTitle))
            {
                errors["Title"] = errors.TryGetValue("Title", out var existing) 
                    ? existing.Append("Title is required.").ToArray() 
                    : new[] { "Title is required." };
            }

            // 4. Валідація Description
            var rawDesc = request.Description ?? string.Empty;
            if (rawDesc.Length > 4000)
            {
                errors["Description"] = new[] { "Description must not exceed 4000 characters." };
            }
            var trimmedDesc = rawDesc.Trim();
            if (string.IsNullOrWhiteSpace(trimmedDesc))
            {
                errors["Description"] = errors.TryGetValue("Description", out var existingDesc) 
                    ? existingDesc.Append("Description is required.").ToArray() 
                    : new[] { "Description is required." };
            }

            // 5. Валідація Severity
            if (!Enum.TryParse<IncidentSeverity>(request.Severity, ignoreCase: true, out var severity) || !Enum.IsDefined(severity))
            {
                errors["Severity"] = new[] { "Invalid severity value." };
            }

            // 6. Валідація OccurredAtUtc
            if (request.OccurredAtUtc is null)
            {
                errors["OccurredAtUtc"] = new[] { "OccurredAtUtc is required." };
            }
            else if (request.OccurredAtUtc > now.AddMinutes(5))
            {
                errors["OccurredAtUtc"] = new[] { "OccurredAtUtc cannot be in the future." };
            }

            // 7. Перевірка наявності помилок до будь-якого звернення до БД
            if (errors.Count > 0)
            {
                return Results.ValidationProblem(errors);
            }

            // 8. Нормалізація
            var finalTitle = trimmedTitle;
            var finalDesc = trimmedDesc;
            var finalOccurredAt = request.OccurredAtUtc!.Value.ToUniversalTime();

            // 10. Створення сутності
            var incident = new Incident
            {
                Id = Guid.NewGuid(),
                OwnerUserId = DbSeeder.AliceId,
                Title = finalTitle,
                Description = finalDesc,
                Severity = severity,
                Status = IncidentStatus.New,
                OccurredAtUtc = finalOccurredAt,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };

            db.Incidents.Add(incident);
            await db.SaveChangesAsync(ct);

            // 11. Повернення response DTO
            return Results.Created(
                $"/api/incidents/{incident.Id}", 
                new CreatedIncidentResponse(incident.Id, incident.Title)
            );
        });
    }

    /// <summary>
    /// Екранує спеціальні символи оператора LIKE/ILIKE (\, _, %) у введеному користувачем рядку.
    /// </summary>
    public static string EscapeLike(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value
            .Replace("\\", "\\\\") // 1. Екрануємо слеш
            .Replace("_", "\\_")   // 2. Екрануємо підкреслення
            .Replace("%", "\\%");  // 3. Екрануємо знак відсотка
    }
}

public sealed record CreateIncidentRequest(
    string? Title, string? Description, string? Severity, DateTimeOffset? OccurredAtUtc);

public sealed record CreatedIncidentResponse(Guid Id, string Title);

public sealed record IncidentSearchItemResponse(
    Guid Id,
    string Title,
    string Description,
    string Severity,
    string Status,
    DateTimeOffset CreatedAtUtc
);