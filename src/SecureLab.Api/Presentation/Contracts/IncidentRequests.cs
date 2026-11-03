namespace SecureLab.Api.Presentation.Contracts;

// Звичайний content update: лише дозволені поля. OwnerUserId, Status, role, Id і службові
// часові поля визначає сервер; невідомі поля JSON ігноруються (контракт A-08a).
public sealed record UpdateIncidentRequest(
    string? Title,
    string? Description,
    string? Severity);

public sealed record IncidentTransitionResponse(Guid Id, string Status);
