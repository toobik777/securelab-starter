using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using SecureLab.Api.Application.Incidents;
using SecureLab.Api.Data;
using SecureLab.Api.Data.Entities;
using SecureLab.Api.Presentation.Contracts;

namespace SecureLab.Api.Presentation.Endpoints;

public static class IncidentEndpoints
{
    public static IEndpointRouteBuilder MapIncidentEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/incidents")
            .WithTags("Incidents");

        group.MapGet("/", GetListAsync)
            .WithName("GetIncidents")
            .Produces<IReadOnlyList<IncidentListItemResponse>>()
            .ProducesValidationProblem();

        group.MapGet("/severity-summary", GetSeveritySummaryAsync)
            .WithName("GetIncidentSeveritySummary")
            .Produces<IReadOnlyList<IncidentSeveritySummaryResponse>>()
            .ProducesValidationProblem();

        group.MapGet("/{id:guid}", GetDetailsAsync)
            .WithName("GetIncidentDetails")
            .Produces<IncidentDetailsResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        // VULNERABLE (ЛР 4, контрольований локальний стан): ресурс завантажується лише за id,
        // без порівняння з principal і без рішення для ролі/ресурсу.
        group.MapPut("/{id:guid}", UpdateAsync).WithName("UpdateIncident");
        group.MapDelete("/{id:guid}", DeleteAsync).WithName("DeleteIncident");
        group.MapPost("/{id:guid}/triage", TriageAsync).WithName("TriageIncident");
        endpoints.MapGet("/api/incident-comments/{id:guid}", GetCommentAsync).WithTags("Incidents");

        return endpoints;
    }

    private static async Task<IResult> UpdateAsync(
        Guid id,
        UpdateIncidentRequest request,
        SecureLabDbContext db,
        CancellationToken cancellationToken)
    {
        var incident = await db.Incidents.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (incident is null)
        {
            return Results.Problem(
                title: "Інцидент не знайдено",
                detail: $"Інцидент '{id}' не існує.",
                statusCode: StatusCodes.Status404NotFound);
        }

        if (request.Title is not null) incident.Title = request.Title;
        if (request.Description is not null) incident.Description = request.Description;
        if (Enum.TryParse<IncidentSeverity>(request.Severity, true, out var severity)) incident.Severity = severity;
        // Дефект: звичайний update приймає protected status і власника з тіла.
        if (Enum.TryParse<IncidentStatus>(request.Status, true, out var status)) incident.Status = status;
        if (request.OwnerUserId is { } owner) incident.OwnerUserId = owner;
        incident.UpdatedAtUtc = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> DeleteAsync(Guid id, SecureLabDbContext db, CancellationToken cancellationToken)
    {
        var incident = await db.Incidents.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (incident is null)
        {
            return Results.Problem(title: "Інцидент не знайдено", detail: $"Інцидент '{id}' не існує.", statusCode: StatusCodes.Status404NotFound);
        }

        db.Incidents.Remove(incident);
        await db.SaveChangesAsync(cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> TriageAsync(
        Guid id,
        HttpContext httpContext,
        SecureLabDbContext db,
        CancellationToken cancellationToken)
    {
        var incident = await db.Incidents.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (incident is null)
        {
            return Results.Problem(title: "Інцидент не знайдено", detail: $"Інцидент '{id}' не існує.", statusCode: StatusCodes.Status404NotFound);
        }

        // Дефект: жодної перевірки ролі Analyst; будь-який клієнт змінює status.
        var changedBy = Guid.TryParse(httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
            ? userId
            : DbSeeder.MorganId;
        db.IncidentStatusHistory.Add(new IncidentStatusHistory
        {
            Id = Guid.NewGuid(),
            IncidentId = incident.Id,
            ChangedByUserId = changedBy,
            OldStatus = incident.Status,
            NewStatus = IncidentStatus.Triaged,
            Note = "Triage",
            CreatedAtUtc = DateTimeOffset.UtcNow
        });
        incident.Status = IncidentStatus.Triaged;
        incident.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return Results.Ok(new { incident.Id, Status = incident.Status.ToString() });
    }

    private static async Task<IResult> GetCommentAsync(Guid id, SecureLabDbContext db, CancellationToken cancellationToken)
    {
        // Дефект: child завантажується лише за власним id, внутрішній коментар теж.
        var comment = await db.IncidentComments
            .AsNoTracking()
            .Where(item => item.Id == id)
            .Select(item => new IncidentCommentResponse(item.Id, item.Author.DisplayName, item.Text, item.CreatedAtUtc))
            .SingleOrDefaultAsync(cancellationToken);
        return comment is null ? Results.NotFound() : Results.Ok(comment);
    }

    private static async Task<IResult> GetListAsync(
        string? status,
        IncidentQueries queries,
        CancellationToken cancellationToken)
    {
        IncidentStatus? parsedStatus = null;
        if (status is not null)
        {
            if (!Enum.TryParse<IncidentStatus>(status, ignoreCase: true, out var candidate)
                || !Enum.IsDefined(candidate))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["status"] = ["Допустимі значення: New, Triaged, InProgress, Resolved, Closed."]
                });
            }

            parsedStatus = candidate;
        }

        return Results.Ok(await queries.GetListAsync(parsedStatus, cancellationToken));
    }

    private static async Task<IResult> GetDetailsAsync(
        Guid id,
        IncidentQueries queries,
        CancellationToken cancellationToken)
    {
        var incident = await queries.GetDetailsAsync(id, cancellationToken);
        return incident is null
            ? Results.Problem(
                title: "Інцидент не знайдено",
                detail: $"Інцидент '{id}' не існує.",
                statusCode: StatusCodes.Status404NotFound)
            : Results.Ok(incident);
    }

    private static async Task<IResult> GetSeveritySummaryAsync(
        string? status,
        IncidentQueries queries,
        CancellationToken cancellationToken)
    {
        IncidentStatus? parsedStatus = null;
        if (status is not null)
        {
            if (!Enum.TryParse<IncidentStatus>(status, ignoreCase: true, out var candidate)
                || !Enum.IsDefined(candidate))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["status"] = ["Допустимі значення: New, Triaged, InProgress, Resolved, Closed."]
                });
            }

            parsedStatus = candidate;
        }

        return Results.Ok(await queries.GetSeveritySummaryAsync(parsedStatus, cancellationToken));
    }
}
