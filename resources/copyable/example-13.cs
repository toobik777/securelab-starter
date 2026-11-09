new IncidentComment
{
    Id = Guid.Parse("30000000-0000-0000-0000-000000000005"),
    Incident = aliceIncident,
    Author = morgan,
    Text = Lab05Marker,
    IsInternal = false,
    CreatedAtUtc = new DateTimeOffset(
        2026, 8, 1, 9, 35, 0, TimeSpan.Zero)
}
