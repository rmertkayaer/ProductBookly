namespace Bookly.Core.Domain;

/// <summary>
/// Throwaway M0 entity: exists only so the skeleton has a real table, a real
/// migration, and a provable Postgres round-trip. Delete when the first real
/// Core entity lands in M1.
/// </summary>
public sealed class SkeletonCheck
{
    public Guid Id { get; set; }

    public string Note { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; }
}
