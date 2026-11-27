using System.Diagnostics;
using System.Diagnostics.Metrics;
using Api.Models;

namespace Api.Services;

/// <summary>
/// Domain traces and metrics for the service desk. The "Contoso.*" wildcard in Extensions.cs exports both.
/// Metric tags are limited to category, priority, status and SLA outcome so cardinality stays bounded;
/// request IDs, titles and people only appear on spans and logs.
/// </summary>
public sealed class ServiceDeskTelemetry
{
    public const string Name = "Contoso.ServiceDesk";
    public static readonly ActivitySource ActivitySource = new(Name);

    private readonly Counter<long> _requestsCreated;
    private readonly Counter<long> _transitions;
    private readonly Histogram<double> _resolutionTime;
    private readonly UpDownCounter<long> _openRequests;

    public ServiceDeskTelemetry(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(Name);

        _requestsCreated = meter.CreateCounter<long>(
            "servicedesk.requests.created", unit: "{request}",
            description: "Service requests raised, by category and priority.");

        _transitions = meter.CreateCounter<long>(
            "servicedesk.request.transitions", unit: "{transition}",
            description: "Status changes requested, by from/to status and whether the move was accepted or rejected.");

        _resolutionTime = meter.CreateHistogram(
            "servicedesk.request.resolution_time", unit: "h",
            description: "Hours from when a request was raised until it was resolved, by priority and SLA outcome.",
            advice: new InstrumentAdvice<double> { HistogramBucketBoundaries = [0.5, 1, 2, 4, 8, 12, 24, 48, 72, 120] });

        _openRequests = meter.CreateUpDownCounter<long>(
            "servicedesk.requests.open", unit: "{request}",
            description: "Requests currently open (New or InProgress), by priority.");
    }

    public void RequestCreated(ServiceRequest request)
    {
        _requestsCreated.Add(1, new TagList
        {
            { "servicedesk.request.category", request.Category.ToString() },
            { "servicedesk.request.priority", request.Priority.ToString() }
        });
        OpenRequestsChanged(request.Priority, 1);
    }

    public void Transition(RequestStatus from, RequestStatus to, bool accepted) =>
        _transitions.Add(1, new TagList
        {
            { "servicedesk.transition.from", from.ToString() },
            { "servicedesk.transition.to", to.ToString() },
            { "servicedesk.transition.outcome", accepted ? "accepted" : "rejected" }
        });

    public void RequestResolved(RequestPriority priority, double hours, bool slaMet) =>
        _resolutionTime.Record(hours, new TagList
        {
            { "servicedesk.request.priority", priority.ToString() },
            { "servicedesk.sla.outcome", slaMet ? "met" : "breached" }
        });

    public void OpenRequestsChanged(RequestPriority priority, long delta) =>
        _openRequests.Add(delta, new KeyValuePair<string, object?>("servicedesk.request.priority", priority.ToString()));
}

internal static class ServiceRequestActivityExtensions
{
    public static Activity? SetRequestTags(this Activity? activity, ServiceRequest request) => activity?
        .SetTag("servicedesk.request.id", request.Id)
        .SetTag("servicedesk.request.category", request.Category.ToString())
        .SetTag("servicedesk.request.priority", request.Priority.ToString())
        .SetTag("servicedesk.request.status", request.Status.ToString())
        .SetTag("servicedesk.request.assignee", request.AssignedTo);

    public static Activity? SetTransitionTags(this Activity? activity, ServiceRequest request, RequestStatus from, RequestStatus to) => activity?
        .SetTag("servicedesk.request.id", request.Id)
        .SetTag("servicedesk.request.priority", request.Priority.ToString())
        .SetTag("servicedesk.request.assignee", request.AssignedTo)
        .SetTag("servicedesk.transition.from", from.ToString())
        .SetTag("servicedesk.transition.to", to.ToString());
}
