namespace DiscoverJesus.Api.Models;

public enum EditorialAuditAction
{
    Published = 1,
    Withdrawn = 2,
    Deleted = 3,
}

public sealed class EditorialAuditRecord
{
    public long Id { get; init; }
    public Guid AccountId { get; init; }
    public EditorialAuditAction Action { get; init; }
    public required string ResourceType { get; init; }
    public required string ResourceId { get; init; }
    public DateTimeOffset OccurredAt { get; init; }
}
