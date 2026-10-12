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
            var order = sortBy switch
            {
                null or "" or "createdAtUtc" => "created_at_utc DESC",
                "severity" => "severity", "status" => "status", _ => sortBy
            };
            var sql = "SELECT * FROM incidents WHERE title ILIKE '%" + (q ?? "")
                + "%' OR description ILIKE '%" + (q ?? "") + "%' ORDER BY " + order + " LIMIT 50";
            var rows = await db.Incidents.FromSqlRaw(sql).AsNoTracking().ToListAsync(ct);
            return Results.Ok(rows.Select(row => new
            {
                row.Id, row.Title, row.Description,
                Severity = row.Severity.ToString(), Status = row.Status.ToString(), row.CreatedAtUtc
            }));
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
}

public sealed record CreateIncidentRequest(
    string? Title, string? Description, string? Severity, DateTimeOffset? OccurredAtUtc);

public sealed record CreatedIncidentResponse(Guid Id, string Title);