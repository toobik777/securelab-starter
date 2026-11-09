using System.Diagnostics;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using SecureLab.Api.Data;
using SecureLab.Api.Data.Entities;

namespace SecureLab.Api.Application.Incidents;

/// <summary>Операції над Incident, для яких сервер ухвалює окреме рішення (докладно: docs/access-matrix.md).</summary>
public static class IncidentOperations
{
    public static readonly OperationAuthorizationRequirement Read = new() { Name = nameof(Read) };
    public static readonly OperationAuthorizationRequirement Update = new() { Name = nameof(Update) };
    public static readonly OperationAuthorizationRequirement Delete = new() { Name = nameof(Delete) };
    public static readonly OperationAuthorizationRequirement Triage = new() { Name = nameof(Triage) };
    public static readonly OperationAuthorizationRequirement ReadInternalComments = new() { Name = nameof(ReadInternalComments) };
}

/// <summary>Єдине джерело правил видимості для рішень над одним ресурсом і для обмеження запитів.</summary>
public static class IncidentAccess
{
    /// <summary>Id суб'єкта береться лише з verified principal, ніколи з body, query чи header.</summary>
    public static bool TryGetUserId(ClaimsPrincipal user, out Guid userId) =>
        Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out userId);

    /// <summary>Analyst читає всі інциденти для аналізу; решта бачить лише власні.</summary>
    public static bool CanSeeAll(ClaimsPrincipal user) => user.IsInRole(DbSeeder.AnalystRole);
}

/// <summary>Resource-based handler: principal + конкретний Incident + операція -> permit/deny.</summary>
public sealed class IncidentAuthorizationHandler
    : AuthorizationHandler<OperationAuthorizationRequirement, Incident>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        OperationAuthorizationRequirement requirement,
        Incident incident)
    {
        if (!IncidentAccess.TryGetUserId(context.User, out var userId))
        {
            return Task.CompletedTask;
        }

        var isOwner = incident.OwnerUserId == userId;
        var isAnalyst = IncidentAccess.CanSeeAll(context.User);

        var allowed = requirement.Name switch
        {
            nameof(IncidentOperations.Read) => isOwner || isAnalyst,
            nameof(IncidentOperations.Update) => isOwner,
            nameof(IncidentOperations.Delete) => isOwner && incident.Status == IncidentStatus.New,
            nameof(IncidentOperations.Triage) => isAnalyst,
            nameof(IncidentOperations.ReadInternalComments) => isAnalyst,
            _ => false
        };

        if (allowed)
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}

/// <summary>
/// Аудиторська подія для відмови в privileged action. Навмисно відсутні: cookie, password, token,
/// тіло запиту, stack trace. TraceId збігається з полем traceId у Problem Details відповіді.
/// </summary>
public static partial class AuthorizationAudit
{
    [LoggerMessage(EventId = 4001, Level = LogLevel.Warning,
        Message = "authorization.denied subject={Subject} action={Action} resource={Resource} result=denied occurredAtUtc={OccurredAtUtc:O} traceId={TraceId}")]
    public static partial void Denied(
        ILogger logger, Guid subject, string action, string resource, DateTimeOffset occurredAtUtc, string traceId);

    public static string TraceId(HttpContext httpContext) =>
        Activity.Current?.Id ?? httpContext.TraceIdentifier;
}
