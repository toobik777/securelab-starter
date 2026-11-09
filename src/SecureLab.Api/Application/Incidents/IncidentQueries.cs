using Microsoft.EntityFrameworkCore;
using SecureLab.Api.Data;
using SecureLab.Api.Data.Entities;
using SecureLab.Api.Presentation.Contracts;

namespace SecureLab.Api.Application.Incidents;

public sealed class IncidentQueries(SecureLabDbContext dbContext, ILogger<IncidentQueries> logger)
{
    public async Task<IReadOnlyList<IncidentListItemResponse>> GetListAsync(
        IncidentStatus? status,
        Guid ownerId,
        bool canSeeAll,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("Loading incidents with status filter {Status}", status);

        // Query scoping: умова за суб'єктом стоїть у самому запиті, чужі рядки не завантажуються.
        var query = dbContext.Incidents
            .AsNoTracking()
            .Where(incident => canSeeAll || incident.OwnerUserId == ownerId);
        if (status is not null)
        {
            query = query.Where(incident => incident.Status == status);
        }

        return await query
            .OrderByDescending(incident => incident.CreatedAtUtc)
            .Select(incident => new IncidentListItemResponse(
                incident.Id,
                incident.Title,
                incident.Severity.ToString(),
                incident.Status.ToString(),
                incident.OccurredAtUtc,
                incident.CreatedAtUtc))
            .ToListAsync(cancellationToken);
    }

    /// <summary>Мінімальний «заголовок» ресурсу для рішення handler: Id, власник, status.</summary>
    public Task<Incident?> GetHeaderAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.Incidents
            .AsNoTracking()
            .Where(incident => incident.Id == id)
            .Select(incident => new Incident
            {
                Id = incident.Id,
                OwnerUserId = incident.OwnerUserId,
                Status = incident.Status,
                Title = string.Empty,
                Description = string.Empty
            })
            .SingleOrDefaultAsync(cancellationToken);

    public Task<CommentWithParent?> GetCommentWithParentAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.IncidentComments
            .AsNoTracking()
            .Where(comment => comment.Id == id)
            .Select(comment => new CommentWithParent(
                new IncidentCommentResponse(
                    comment.Id,
                    comment.Author.DisplayName,
                    comment.Text,
                    comment.CreatedAtUtc),
                comment.IsInternal,
                new Incident
                {
                    Id = comment.Incident.Id,
                    OwnerUserId = comment.Incident.OwnerUserId,
                    Status = comment.Incident.Status,
                    Title = string.Empty,
                    Description = string.Empty
                }))
            .SingleOrDefaultAsync(cancellationToken);

    public Task<IncidentDetailsResponse?> GetDetailsAsync(
        Guid id,
        bool includeInternalComments,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("Loading incident {IncidentId}", id);

        return dbContext.Incidents
            .AsNoTracking()
            .Where(incident => incident.Id == id)
            .Select(incident => new IncidentDetailsResponse(
                incident.Id,
                incident.Title,
                incident.Description,
                incident.Severity.ToString(),
                incident.Status.ToString(),
                incident.OccurredAtUtc,
                incident.CreatedAtUtc,
                incident.Owner.DisplayName,
                incident.Comments
                    .Where(comment => includeInternalComments || !comment.IsInternal)
                    .OrderBy(comment => comment.CreatedAtUtc)
                    .Select(comment => new IncidentCommentResponse(
                        comment.Id,
                        comment.Author.DisplayName,
                        comment.Text,
                        comment.CreatedAtUtc))
                    .ToList()))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<IncidentSeveritySummaryResponse>> GetSeveritySummaryAsync(
        IncidentStatus? status,
        Guid ownerId,
        bool canSeeAll,
        CancellationToken cancellationToken)
    {
        // Обмеження за суб'єктом стоїть до групування: чужі записи не потрапляють у лічильники.
        var query = dbContext.Incidents
            .AsNoTracking()
            .Where(incident => canSeeAll || incident.OwnerUserId == ownerId);
        if (status is not null)
        {
            query = query.Where(incident => incident.Status == status);
        }

        var counts = await query
            .GroupBy(incident => incident.Severity)
            .Select(g => new { Severity = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        logger.LogInformation(
            "Severity summary computed for {GroupCount} groups with status filter {Status}",
            counts.Count,
            status);

        return Enum.GetValues<IncidentSeverity>()
            .Select(severity => new IncidentSeveritySummaryResponse(
                severity.ToString(),
                counts.FirstOrDefault(c => c.Severity == severity)?.Count ?? 0))
            .ToList();
    }
}

/// <summary>Child + мінімум даних про parent для централізованого рішення.</summary>
public sealed record CommentWithParent(IncidentCommentResponse Comment, bool IsInternal, Incident Parent);
