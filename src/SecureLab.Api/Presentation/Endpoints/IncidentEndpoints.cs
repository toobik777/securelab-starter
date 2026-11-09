using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
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
        // Вся група вимагає authentication (401 для анонімного); авторизація ресурсу — нижче, у handler.
        var group = endpoints.MapGroup("/api/incidents")
            .WithTags("Incidents")
            .RequireAuthorization();

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

        group.MapPut("/{id:guid}", UpdateAsync).WithName("UpdateIncident");
        group.MapDelete("/{id:guid}", DeleteAsync).WithName("DeleteIncident");
        group.MapPost("/{id:guid}/triage", TriageAsync).WithName("TriageIncident");

        // Child resource: рішення приймається через parent incident.
        endpoints.MapGet("/api/incident-comments/{id:guid}", GetCommentAsync)
            .WithTags("Incidents")
            .WithName("GetIncidentComment")
            .RequireAuthorization();

        return endpoints;
    }

    // Політика видимості (docs/access-matrix.md): неіснуючий ресурс і ресурс, який principal не має права
    // читати, дають ту саму відповідь 404 (один helper => однакові status, Content-Type і тіло).
    private static IResult IncidentNotFound() => Results.Problem(
        title: "Інцидент не знайдено",
        statusCode: StatusCodes.Status404NotFound);

    private static IResult CommentNotFound() => Results.Problem(
        title: "Коментар не знайдено",
        statusCode: StatusCodes.Status404NotFound);

    private static IResult InvalidStatusFilter() => Results.ValidationProblem(new Dictionary<string, string[]>
    {
        ["status"] = ["Допустимі значення: New, Triaged, InProgress, Resolved, Closed."]
    });

    private static bool TryParseStatus(string? status, out IncidentStatus? parsed)
    {
        parsed = null;
        if (status is null)
        {
            return true;
        }

        if (!Enum.TryParse<IncidentStatus>(status, ignoreCase: true, out var candidate)
            || !Enum.IsDefined(candidate))
        {
            return false;
        }

        parsed = candidate;
        return true;
    }

    /// <summary>
    /// Централізоване рішення для одного інциденту. Порядок: ресурс існує -> principal може його читати
    /// (інакше 404, як для неіснуючого) -> principal може виконати операцію (інакше 403).
    /// Перевірка передує будь-якій зміні стану; для privileged action відмова журналюється.
    /// </summary>
    private static async Task<(Incident? Incident, IResult? Failure)> DecideAsync(
        Incident? incident,
        OperationAuthorizationRequirement operation,
        string action,
        bool audit,
        HttpContext httpContext,
        IAuthorizationService authorization,
        ILogger logger)
    {
        if (incident is null)
        {
            return (null, IncidentNotFound());
        }

        var canRead = await authorization.AuthorizeAsync(httpContext.User, incident, IncidentOperations.Read);
        if (!canRead.Succeeded)
        {
            LogDenied(audit, action, incident.Id, httpContext, logger);
            return (null, IncidentNotFound());
        }

        if (!ReferenceEquals(operation, IncidentOperations.Read))
        {
            var decision = await authorization.AuthorizeAsync(httpContext.User, incident, operation);
            if (!decision.Succeeded)
            {
                LogDenied(audit, action, incident.Id, httpContext, logger);
                return (null, Results.Forbid());
            }
        }

        return (incident, null);
    }

    private static void LogDenied(bool audit, string action, Guid resourceId, HttpContext httpContext, ILogger logger)
    {
        if (audit && IncidentAccess.TryGetUserId(httpContext.User, out var subject))
        {
            AuthorizationAudit.Denied(
                logger, subject, action, $"incident:{resourceId}", DateTimeOffset.UtcNow,
                AuthorizationAudit.TraceId(httpContext));
        }
    }

    private static async Task<IResult> GetListAsync(
        string? status,
        HttpContext httpContext,
        IncidentQueries queries,
        CancellationToken cancellationToken)
    {
        if (!TryParseStatus(status, out var parsedStatus))
        {
            return InvalidStatusFilter();
        }

        if (!IncidentAccess.TryGetUserId(httpContext.User, out var userId))
        {
            return Results.Challenge();
        }

        return Results.Ok(await queries.GetListAsync(
            parsedStatus, userId, IncidentAccess.CanSeeAll(httpContext.User), cancellationToken));
    }

    private static async Task<IResult> GetSeveritySummaryAsync(
        string? status,
        HttpContext httpContext,
        IncidentQueries queries,
        CancellationToken cancellationToken)
    {
        if (!TryParseStatus(status, out var parsedStatus))
        {
            return InvalidStatusFilter();
        }

        if (!IncidentAccess.TryGetUserId(httpContext.User, out var userId))
        {
            return Results.Challenge();
        }

        return Results.Ok(await queries.GetSeveritySummaryAsync(
            parsedStatus, userId, IncidentAccess.CanSeeAll(httpContext.User), cancellationToken));
    }

    private static async Task<IResult> GetDetailsAsync(
        Guid id,
        HttpContext httpContext,
        IncidentQueries queries,
        IAuthorizationService authorization,
        ILogger<IncidentAuthorizationHandler> logger,
        CancellationToken cancellationToken)
    {
        var header = await queries.GetHeaderAsync(id, cancellationToken);
        var (incident, failure) = await DecideAsync(
            header, IncidentOperations.Read, "incident.read", audit: false, httpContext, authorization, logger);
        if (failure is not null)
        {
            return failure;
        }

        // Internal comments бачить лише роль, якій це дозволено матрицею; рішення приймає сервер.
        var includeInternal = (await authorization.AuthorizeAsync(
            httpContext.User, incident!, IncidentOperations.ReadInternalComments)).Succeeded;

        var details = await queries.GetDetailsAsync(id, includeInternal, cancellationToken);
        return details is null ? IncidentNotFound() : Results.Ok(details);
    }

    private static async Task<IResult> UpdateAsync(
        Guid id,
        UpdateIncidentRequest request,
        HttpContext httpContext,
        SecureLabDbContext db,
        IAuthorizationService authorization,
        ILogger<IncidentAuthorizationHandler> logger,
        CancellationToken cancellationToken)
    {
        // Tracked entity (без AsNoTracking) — інакше SaveChangesAsync нічого не запише.
        var entity = await db.Incidents.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        var (incident, failure) = await DecideAsync(
            entity, IncidentOperations.Update, "incident.update", audit: false, httpContext, authorization, logger);
        if (failure is not null)
        {
            return failure;
        }

        var errors = new Dictionary<string, string[]>();
        var title = request.Title?.Trim();
        var description = request.Description?.Trim();
        IncidentSeverity? severity = null;

        if (request.Title is not null && (string.IsNullOrWhiteSpace(title) || title.Length > 160))
        {
            errors["Title"] = ["Title must be 1-160 characters."];
        }

        if (request.Description is not null && (string.IsNullOrWhiteSpace(description) || description.Length > 4000))
        {
            errors["Description"] = ["Description must be 1-4000 characters."];
        }

        if (request.Severity is not null)
        {
            if (Enum.TryParse<IncidentSeverity>(request.Severity, ignoreCase: true, out var parsed)
                && Enum.IsDefined(parsed))
            {
                severity = parsed;
            }
            else
            {
                errors["Severity"] = ["Invalid severity value."];
            }
        }

        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        // Змінюються лише дозволені content fields. Owner, Status, Id і службові поля — на сервері.
        if (title is not null) incident!.Title = title;
        if (description is not null) incident!.Description = description;
        if (severity is not null) incident!.Severity = severity.Value;
        incident!.UpdatedAtUtc = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> DeleteAsync(
        Guid id,
        HttpContext httpContext,
        SecureLabDbContext db,
        IAuthorizationService authorization,
        ILogger<IncidentAuthorizationHandler> logger,
        CancellationToken cancellationToken)
    {
        var entity = await db.Incidents.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        var (incident, failure) = await DecideAsync(
            entity, IncidentOperations.Delete, "incident.delete", audit: false, httpContext, authorization, logger);
        if (failure is not null)
        {
            return failure;
        }

        db.Incidents.Remove(incident!);
        await db.SaveChangesAsync(cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> TriageAsync(
        Guid id,
        HttpContext httpContext,
        SecureLabDbContext db,
        IAuthorizationService authorization,
        ILogger<IncidentAuthorizationHandler> logger,
        CancellationToken cancellationToken)
    {
        // Порядок: tracked entity -> рішення (при deny: audit + відмова) -> допустимість поточного
        // status -> сервер визначає наступний status -> SaveChangesAsync.
        var entity = await db.Incidents.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        var (incident, failure) = await DecideAsync(
            entity, IncidentOperations.Triage, "incident.triage", audit: true, httpContext, authorization, logger);
        if (failure is not null)
        {
            return failure;
        }

        if (incident!.Status != IncidentStatus.New)
        {
            return Results.Problem(
                title: "Перехід не дозволений",
                detail: "Triage можливий лише для інциденту у стані New.",
                statusCode: StatusCodes.Status409Conflict);
        }

        IncidentAccess.TryGetUserId(httpContext.User, out var analystId);
        var now = DateTimeOffset.UtcNow;
        db.IncidentStatusHistory.Add(new IncidentStatusHistory
        {
            Id = Guid.NewGuid(),
            IncidentId = incident.Id,
            ChangedByUserId = analystId,
            OldStatus = incident.Status,
            NewStatus = IncidentStatus.Triaged,
            Note = "Первинна оцінка (triage).",
            CreatedAtUtc = now
        });
        incident.Status = IncidentStatus.Triaged; // next status визначає сервер, не клієнт
        incident.UpdatedAtUtc = now;

        await db.SaveChangesAsync(cancellationToken);
        return Results.Ok(new IncidentTransitionResponse(incident.Id, incident.Status.ToString()));
    }

    private static async Task<IResult> GetCommentAsync(
        Guid id,
        HttpContext httpContext,
        IncidentQueries queries,
        IAuthorizationService authorization,
        CancellationToken cancellationToken)
    {
        var model = await queries.GetCommentWithParentAsync(id, cancellationToken);
        if (model is null)
        {
            return CommentNotFound();
        }

        // Child не завантажується «за власним id»: спершу рішення для parent incident.
        var canReadParent = await authorization.AuthorizeAsync(httpContext.User, model.Parent, IncidentOperations.Read);
        if (!canReadParent.Succeeded)
        {
            return CommentNotFound();
        }

        if (model.IsInternal)
        {
            var canReadInternal = await authorization.AuthorizeAsync(
                httpContext.User, model.Parent, IncidentOperations.ReadInternalComments);
            if (!canReadInternal.Succeeded)
            {
                return CommentNotFound();
            }
        }

        return Results.Ok(model.Comment);
    }
}
