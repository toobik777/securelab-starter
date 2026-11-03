namespace SecureLab.Api.Presentation.Contracts;

// VULNERABLE (ЛР 4): звичайний update DTO містить protected поля Status і OwnerUserId.
public sealed record UpdateIncidentRequest(
    string? Title,
    string? Description,
    string? Severity,
    string? Status,
    Guid? OwnerUserId);
