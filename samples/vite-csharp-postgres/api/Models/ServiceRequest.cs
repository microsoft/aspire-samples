namespace Api.Models;

public enum RequestCategory
{
    Hardware,
    Software,
    Access,
    Network
}

public enum RequestPriority
{
    Low,
    Medium,
    High,
    Critical
}

public enum RequestStatus
{
    New,
    InProgress,
    Resolved
}

/// <summary>An IT service desk request (ticket) stored in PostgreSQL.</summary>
public class ServiceRequest
{
    public const int MaxTitleLength = 200;
    public const int MaxPersonNameLength = 80;

    public int Id { get; set; }
    public required string Title { get; set; }
    public RequestCategory Category { get; set; }
    public RequestPriority Priority { get; set; }
    public RequestStatus Status { get; set; }
    public required string RequestedBy { get; set; }
    public string? AssignedTo { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime DueAt { get; set; }
    public DateTime? ResolvedAt { get; set; }

    public bool IsOpen => Status != RequestStatus.Resolved;

    public bool IsOverdue(DateTime now) => IsOpen && DueAt < now;

    /// <summary>Response-time targets: how long each priority may stay open before it breaches its SLA.</summary>
    public static TimeSpan SlaFor(RequestPriority priority) => priority switch
    {
        RequestPriority.Critical => TimeSpan.FromHours(4),
        RequestPriority.High => TimeSpan.FromHours(8),
        RequestPriority.Medium => TimeSpan.FromHours(24),
        _ => TimeSpan.FromHours(72)
    };
}