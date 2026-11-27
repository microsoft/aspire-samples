using System.Diagnostics;
using Api.Data;
using Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Api.Services;

/// <summary>
/// Raises, triages and resolves service requests. Every change runs in a domain span (so the EF Core / Npgsql
/// SQL spans nest under it) and records the <see cref="ServiceDeskTelemetry"/> metrics and a structured log.
/// </summary>
public sealed class RequestQueue(
    ServiceDeskDbContext db,
    ServiceDeskTelemetry telemetry,
    TimeProvider timeProvider,
    ILogger<RequestQueue> logger)
{
    public DateTime UtcNow => timeProvider.GetUtcNow().UtcDateTime;

    public Task<List<ServiceRequest>> ListAsync(
        RequestStatus? status, RequestPriority? priority, int offset, int limit, CancellationToken cancellationToken)
    {
        var query = db.Requests.AsNoTracking();

        if (status is { } s)
        {
            query = query.Where(r => r.Status == s);
        }

        if (priority is { } p)
        {
            query = query.Where(r => r.Priority == p);
        }

        // Open work first (most urgent, then soonest due), followed by the most recently resolved.
        return query
            .OrderBy(r => r.Status == RequestStatus.Resolved)
            .ThenByDescending(r => r.ResolvedAt)
            .ThenByDescending(r => r.Priority)
            .ThenBy(r => r.DueAt)
            .Skip(offset)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public ValueTask<ServiceRequest?> FindAsync(int id, CancellationToken cancellationToken) =>
        db.Requests.FindAsync([id], cancellationToken);

    public async Task<QueueSummary> SummarizeAsync(CancellationToken cancellationToken)
    {
        var now = UtcNow;
        var since = now.AddHours(-24);

        var rows = await db.Requests
            .AsNoTracking()
            .Where(r => r.Status != RequestStatus.Resolved || r.ResolvedAt >= since)
            .Select(r => new { r.Status, r.Priority, r.DueAt })
            .ToListAsync(cancellationToken);

        var open = rows.Where(r => r.Status != RequestStatus.Resolved).ToList();

        return new QueueSummary(
            Open: open.Count,
            Unassigned: open.Count(r => r.Status == RequestStatus.New),
            InProgress: open.Count(r => r.Status == RequestStatus.InProgress),
            Critical: open.Count(r => r.Priority == RequestPriority.Critical),
            Overdue: open.Count(r => r.DueAt < now),
            ResolvedLast24Hours: rows.Count - open.Count);
    }

    /// <param name="raisedAt">Backdates the request (used by the traffic simulator); defaults to now.</param>
    public async Task<ServiceRequest> RaiseAsync(
        string title, string requestedBy, RequestCategory category, RequestPriority priority,
        CancellationToken cancellationToken, DateTime? raisedAt = null)
    {
        using var activity = ServiceDeskTelemetry.ActivitySource.StartActivity("servicedesk.raise_request");

        var createdAt = raisedAt ?? UtcNow;
        var sla = ServiceRequest.SlaFor(priority);
        var request = new ServiceRequest
        {
            Title = title,
            RequestedBy = requestedBy,
            Category = category,
            Priority = priority,
            Status = RequestStatus.New,
            CreatedAt = createdAt,
            DueAt = createdAt + sla
        };

        db.Requests.Add(request);
        await db.SaveChangesAsync(cancellationToken);

        activity.SetRequestTags(request)?.SetTag("servicedesk.sla.target_hours", sla.TotalHours);
        telemetry.RequestCreated(request);
        logger.RequestRaised(request.Id, request.Title, request.RequestedBy, request.Priority, request.Category, request.DueAt);

        return request;
    }

    /// <summary>Applies the changes, including at most one status transition.</summary>
    /// <returns><see langword="null"/> when the changes were saved; otherwise why the status change conflicts with the request's state.</returns>
    public async Task<string?> UpdateAsync(ServiceRequest request, UpdateServiceRequest changes, CancellationToken cancellationToken)
    {
        using var activity = ServiceDeskTelemetry.ActivitySource.StartActivity("servicedesk.update_request");
        activity.SetRequestTags(request);

        var target = changes.Status is { } status && status != request.Status ? status : (RequestStatus?)null;

        if (target is { } rejectedTarget && RejectionReason(request, rejectedTarget) is { } reason)
        {
            RecordRejectedTransition(request, rejectedTarget, reason);
            return reason;
        }

        if (changes.Title is { } title)
        {
            request.Title = title;
        }

        if (changes.AssignedTo is { } assignee)
        {
            request.AssignedTo = assignee.Length == 0 ? null : assignee;
        }

        RequestPriority? previousPriority = changes.Priority is { } priority && priority != request.Priority ? request.Priority : null;
        if (previousPriority is not null)
        {
            request.Priority = changes.Priority!.Value;
        }

        if (target is { } to)
        {
            await TransitionAsync(request, to, cancellationToken);
        }
        else
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        if (previousPriority is { } from)
        {
            if (request.IsOpen)
            {
                telemetry.OpenRequestsChanged(from, -1);
                telemetry.OpenRequestsChanged(request.Priority, 1);
            }

            activity?.AddEvent(new ActivityEvent("request.reprioritized", tags: new ActivityTagsCollection
            {
                ["servicedesk.priority.from"] = from.ToString(),
                ["servicedesk.priority.to"] = request.Priority.ToString()
            }));
            logger.PriorityChanged(request.Id, from, request.Priority);
        }

        activity.SetRequestTags(request);
        return null;
    }

    public async Task DeleteAsync(ServiceRequest request, CancellationToken cancellationToken)
    {
        using var activity = ServiceDeskTelemetry.ActivitySource.StartActivity("servicedesk.delete_request");
        activity.SetRequestTags(request);

        db.Requests.Remove(request);
        await db.SaveChangesAsync(cancellationToken);

        if (request.IsOpen)
        {
            telemetry.OpenRequestsChanged(request.Priority, -1);
        }

        logger.RequestDeleted(request.Id, request.Title);
    }

    /// <summary>Replaces every request with <paramref name="requests"/> (used by the Seed sample data command).</summary>
    public async Task ReplaceAllAsync(IReadOnlyList<ServiceRequest> requests, CancellationToken cancellationToken)
    {
        using var activity = ServiceDeskTelemetry.ActivitySource.StartActivity("servicedesk.seed");

        var openBefore = await db.Requests
            .Where(r => r.Status != RequestStatus.Resolved)
            .GroupBy(r => r.Priority)
            .Select(g => new { Priority = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        await db.Requests.ExecuteDeleteAsync(cancellationToken);
        db.Requests.AddRange(requests);
        await db.SaveChangesAsync(cancellationToken);

        foreach (var group in openBefore)
        {
            telemetry.OpenRequestsChanged(group.Priority, -group.Count);
        }

        foreach (var request in requests.Where(r => r.IsOpen))
        {
            telemetry.OpenRequestsChanged(request.Priority, 1);
        }

        activity?.SetTag("servicedesk.seed.requests", requests.Count);
    }

    private async Task TransitionAsync(ServiceRequest request, RequestStatus to, CancellationToken cancellationToken)
    {
        var from = request.Status;
        var previousAssignee = request.AssignedTo;
        var now = UtcNow;

        using var activity = ServiceDeskTelemetry.ActivitySource.StartActivity("servicedesk.transition");

        request.Status = to;
        switch (to)
        {
            case RequestStatus.Resolved:
                request.ResolvedAt = now;
                break;
            case RequestStatus.InProgress when from == RequestStatus.Resolved:
                request.ResolvedAt = null;
                break;
            case RequestStatus.New:
                request.AssignedTo = null;
                break;
        }

        await db.SaveChangesAsync(cancellationToken);

        activity.SetTransitionTags(request, from, to)?
            .AddEvent(new ActivityEvent("request.status_changed", tags: new ActivityTagsCollection
            {
                ["servicedesk.transition.from"] = from.ToString(),
                ["servicedesk.transition.to"] = to.ToString()
            }));
        telemetry.Transition(from, to, accepted: true);

        var hoursOpen = Math.Round((now - request.CreatedAt).TotalHours, 1);

        switch ((from, to))
        {
            case (RequestStatus.New, RequestStatus.InProgress):
                activity?.AddEvent(new ActivityEvent("request.assigned", tags: new ActivityTagsCollection
                {
                    ["servicedesk.request.assignee"] = request.AssignedTo,
                    ["servicedesk.request.wait_hours"] = hoursOpen
                }));
                logger.RequestPickedUp(request.Id, request.AssignedTo, hoursOpen);
                break;

            case (RequestStatus.InProgress, RequestStatus.Resolved):
                var slaMet = now <= request.DueAt;
                activity?
                    .SetTag("servicedesk.request.resolution_hours", hoursOpen)
                    .SetTag("servicedesk.sla.outcome", slaMet ? "met" : "breached");
                telemetry.RequestResolved(request.Priority, (now - request.CreatedAt).TotalHours, slaMet);
                telemetry.OpenRequestsChanged(request.Priority, -1);

                if (slaMet)
                {
                    logger.RequestResolved(request.Id, request.AssignedTo, hoursOpen, request.Priority);
                }
                else
                {
                    var overdueHours = Math.Round((now - request.DueAt).TotalHours, 1);
                    activity?.AddEvent(new ActivityEvent("request.sla_breached", tags: new ActivityTagsCollection
                    {
                        ["servicedesk.sla.overdue_hours"] = overdueHours
                    }));
                    logger.RequestResolvedLate(request.Id, request.AssignedTo, hoursOpen, overdueHours, request.Priority);
                }
                break;

            case (RequestStatus.InProgress, RequestStatus.New):
                activity?.AddEvent(new ActivityEvent("request.released"));
                logger.RequestReleased(request.Id, previousAssignee);
                break;

            case (RequestStatus.Resolved, RequestStatus.InProgress):
                telemetry.OpenRequestsChanged(request.Priority, 1);
                activity?.AddEvent(new ActivityEvent("request.reopened"));
                logger.RequestReopened(request.Id, request.AssignedTo);
                break;
        }
    }

    private void RecordRejectedTransition(ServiceRequest request, RequestStatus to, string reason)
    {
        using (var activity = ServiceDeskTelemetry.ActivitySource.StartActivity("servicedesk.transition"))
        {
            activity.SetTransitionTags(request, request.Status, to)?
                .AddEvent(new ActivityEvent("transition.rejected", tags: new ActivityTagsCollection
                {
                    ["servicedesk.rejection.reason"] = reason
                }))
                .SetStatus(ActivityStatusCode.Error, "Invalid status transition");
        }

        telemetry.Transition(request.Status, to, accepted: false);
        logger.TransitionRejected(request.Id, request.Status, to, reason);
    }

    private static string? RejectionReason(ServiceRequest request, RequestStatus to) => (request.Status, to) switch
    {
        (RequestStatus.New, RequestStatus.InProgress)
            or (RequestStatus.InProgress, RequestStatus.Resolved)
            or (RequestStatus.InProgress, RequestStatus.New)
            or (RequestStatus.Resolved, RequestStatus.InProgress) => null,
        (RequestStatus.New, RequestStatus.Resolved) =>
            $"Request SR-{request.Id} hasn't been picked up yet. Assign it and move it to InProgress before resolving it.",
        (RequestStatus.Resolved, RequestStatus.New) =>
            $"Request SR-{request.Id} is already resolved. Reopen it as InProgress instead.",
        _ => $"Request SR-{request.Id} can't move from {request.Status} to {to}."
    };
}
