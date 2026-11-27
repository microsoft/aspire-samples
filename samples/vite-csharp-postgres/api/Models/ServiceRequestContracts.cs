using System.ComponentModel;

namespace Api.Models;

[Description("An IT service desk request.")]
public record ServiceRequestDto(
    [property: Description("The request ID. The UI shows it as `SR-{id}`.")] int Id,
    [property: Description("A short summary of the problem or ask.")] string Title,
    [property: Description("The kind of help needed.")] RequestCategory Category,
    [property: Description("Business impact. Sets the SLA: Critical 4 h, High 8 h, Medium 24 h, Low 72 h.")] RequestPriority Priority,
    [property: Description("Where the request is in its lifecycle: `New` → `InProgress` → `Resolved`.")] RequestStatus Status,
    [property: Description("The employee who raised the request.")] string RequestedBy,
    [property: Description("The service desk agent working on the request, if any.")] string? AssignedTo,
    [property: Description("When the request was raised (UTC).")] DateTime CreatedAt,
    [property: Description("When the request breaches its SLA (UTC).")] DateTime DueAt,
    [property: Description("When the request was resolved (UTC), if it has been.")] DateTime? ResolvedAt,
    [property: Description("Whether the request is still open after its SLA due time.")] bool IsOverdue)
{
    public static ServiceRequestDto From(ServiceRequest request, DateTime now) => new(
        request.Id,
        request.Title,
        request.Category,
        request.Priority,
        request.Status,
        request.RequestedBy,
        request.AssignedTo,
        request.CreatedAt,
        request.DueAt,
        request.ResolvedAt,
        request.IsOverdue(now));
}

[Description("A new service desk request.")]
public record CreateServiceRequest(
    [property: Description("A short summary of the problem or ask (1-200 characters).")] string? Title,
    [property: Description("The employee raising the request (1-80 characters).")] string? RequestedBy,
    [property: Description("The kind of help needed. Defaults to `Software`.")] RequestCategory Category = RequestCategory.Software,
    [property: Description("Business impact, which sets the SLA due time. Defaults to `Medium`.")] RequestPriority Priority = RequestPriority.Medium);

[Description("Changes to a service desk request. Omit a property to leave it unchanged.")]
public record UpdateServiceRequest(
    [property: Description("A new title (1-200 characters).")] string? Title = null,
    [property: Description("A new priority. Changing it doesn't move the SLA due time.")] RequestPriority? Priority = null,
    [property: Description("The next status. Allowed moves: `New` → `InProgress` (requires an assignee), `InProgress` → `Resolved` or back to `New`, and `Resolved` → `InProgress` (reopen). Anything else returns 409.")] RequestStatus? Status = null,
    [property: Description("The agent to assign (up to 80 characters). Use an empty string to unassign.")] string? AssignedTo = null);

[Description("Headline numbers for the request queue.")]
public record QueueSummary(
    [property: Description("Requests that are `New` or `InProgress`.")] int Open,
    [property: Description("Open requests nobody has picked up yet (`New`).")] int Unassigned,
    [property: Description("Requests an agent is working on.")] int InProgress,
    [property: Description("Open requests with `Critical` priority.")] int Critical,
    [property: Description("Open requests past their SLA due time.")] int Overdue,
    [property: Description("Requests resolved in the last 24 hours.")] int ResolvedLast24Hours);

[Description("The result of the Seed sample data command.")]
public record SeedResult(
    [property: Description("Number of sample requests inserted.")] int Requests,
    [property: Description("The queue after seeding.")] QueueSummary Queue);

[Description("The result of the Simulate traffic command.")]
public record SimulationResult(
    [property: Description("Number of simulated rounds.")] int Rounds,
    [property: Description("Requests raised by simulated employees.")] int Raised,
    [property: Description("Requests picked up by simulated agents.")] int PickedUp,
    [property: Description("Requests resolved.")] int Resolved,
    [property: Description("Resolved requests reopened because the problem came back.")] int Reopened,
    [property: Description("Invalid moves the API rejected with 409 (an agent tried to resolve a request nobody had picked up).")] int Rejected,
    [property: Description("The queue after the simulation.")] QueueSummary Queue);