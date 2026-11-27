using Api.Models;

namespace Api;

internal static partial class Log
{
    [LoggerMessage(1001, LogLevel.Information, "Request SR-{ServiceRequestId} '{Title}' raised by {RequestedBy} ({Priority} {Category}, due {DueAt})")]
    public static partial void RequestRaised(this ILogger logger, int serviceRequestId, string title, string requestedBy, RequestPriority priority, RequestCategory category, DateTime dueAt);

    [LoggerMessage(1002, LogLevel.Information, "Request SR-{ServiceRequestId} picked up by {AssignedTo} after {WaitHours} h in the queue")]
    public static partial void RequestPickedUp(this ILogger logger, int serviceRequestId, string? assignedTo, double waitHours);

    [LoggerMessage(1003, LogLevel.Information, "Request SR-{ServiceRequestId} resolved by {AssignedTo} in {ResolutionHours} h, within its {Priority} SLA")]
    public static partial void RequestResolved(this ILogger logger, int serviceRequestId, string? assignedTo, double resolutionHours, RequestPriority priority);

    [LoggerMessage(1004, LogLevel.Warning, "Request SR-{ServiceRequestId} resolved by {AssignedTo} in {ResolutionHours} h, {OverdueHours} h past its {Priority} SLA")]
    public static partial void RequestResolvedLate(this ILogger logger, int serviceRequestId, string? assignedTo, double resolutionHours, double overdueHours, RequestPriority priority);

    [LoggerMessage(1005, LogLevel.Information, "Request SR-{ServiceRequestId} reopened and returned to {AssignedTo}")]
    public static partial void RequestReopened(this ILogger logger, int serviceRequestId, string? assignedTo);

    [LoggerMessage(1006, LogLevel.Information, "Request SR-{ServiceRequestId} released back to the queue by {PreviousAssignee}")]
    public static partial void RequestReleased(this ILogger logger, int serviceRequestId, string? previousAssignee);

    [LoggerMessage(1007, LogLevel.Warning, "Rejected move of request SR-{ServiceRequestId} from {FromStatus} to {ToStatus}: {Reason}")]
    public static partial void TransitionRejected(this ILogger logger, int serviceRequestId, RequestStatus fromStatus, RequestStatus toStatus, string reason);

    [LoggerMessage(1008, LogLevel.Information, "Request SR-{ServiceRequestId} re-prioritized from {OldPriority} to {NewPriority}")]
    public static partial void PriorityChanged(this ILogger logger, int serviceRequestId, RequestPriority oldPriority, RequestPriority newPriority);

    [LoggerMessage(1009, LogLevel.Information, "Request SR-{ServiceRequestId} '{Title}' deleted")]
    public static partial void RequestDeleted(this ILogger logger, int serviceRequestId, string title);

    [LoggerMessage(1010, LogLevel.Warning, "Request SR-{ServiceRequestId} was not found")]
    public static partial void RequestNotFound(this ILogger logger, int serviceRequestId);

    [LoggerMessage(1011, LogLevel.Warning, "Rejected invalid request: {ValidationError}")]
    public static partial void ValidationFailed(this ILogger logger, string validationError);

    [LoggerMessage(1020, LogLevel.Information, "Seeded the queue with {RequestCount} sample requests ({OpenCount} open, {OverdueCount} overdue)")]
    public static partial void QueueSeeded(this ILogger logger, int requestCount, int openCount, int overdueCount);

    [LoggerMessage(1021, LogLevel.Information, "Simulated {Rounds} rounds of service desk traffic: {Raised} raised, {PickedUp} picked up, {Resolved} resolved, {Reopened} reopened, {Rejected} rejected")]
    public static partial void TrafficSimulated(this ILogger logger, int rounds, int raised, int pickedUp, int resolved, int reopened, int rejected);

    [LoggerMessage(1030, LogLevel.Information, "Service desk database is ready with {OpenCount} open requests")]
    public static partial void DatabaseReady(this ILogger logger, int openCount);

    [LoggerMessage(1031, LogLevel.Error, "Service desk database initialization failed")]
    public static partial void DatabaseInitializationFailed(this ILogger logger, Exception exception);
}
