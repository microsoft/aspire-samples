using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Text.Json.Nodes;
using Api.Models;
using Api.Services;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Api.Extensions;

public static class ServiceRequestEndpoints
{
    private const int DefaultLimit = 50;
    private const int MaxLimit = 100;

    public static WebApplication MapServiceRequests(this WebApplication app)
    {
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(ServiceRequestEndpoints).FullName!);

        var requests = app.MapGroup("/api/requests").WithTags("Requests");

        requests.MapGet("", async Task<Results<Ok<List<ServiceRequestDto>>, ValidationProblem>> (
                [Description("Only return requests with this status.")] RequestStatus? status,
                [Description("Only return requests with this priority.")] RequestPriority? priority,
                RequestQueue queue,
                CancellationToken cancellationToken,
                [Description("Number of requests to skip.")][Range(0, int.MaxValue)] int offset = 0,
                [Description("Maximum number of requests to return (1-100).")][Range(1, MaxLimit)] int limit = DefaultLimit) =>
            {
                if (offset < 0)
                {
                    return Invalid(logger, "offset", "Offset must be greater than or equal to 0.");
                }

                if (limit is < 1 or > MaxLimit)
                {
                    return Invalid(logger, "limit", $"Limit must be between 1 and {MaxLimit}.");
                }

                var items = await queue.ListAsync(status, priority, offset, limit, cancellationToken);
                var now = queue.UtcNow;
                return TypedResults.Ok(items.Select(r => ServiceRequestDto.From(r, now)).ToList());
            })
            .WithName("ListServiceRequests")
            .WithSummary("List service requests")
            .WithDescription("""
                Returns the request queue: open requests first (most urgent priority, then soonest SLA due time),
                followed by the most recently resolved. Filter by `status` and `priority`, and page with `offset` and `limit`.
                """);

        requests.MapGet("/{id:int}", async Task<Results<Ok<ServiceRequestDto>, ProblemHttpResult>> (
                [Description("The request ID.")] int id,
                RequestQueue queue,
                CancellationToken cancellationToken) =>
            {
                Activity.Current?.SetTag("servicedesk.request.id", id);

                var request = await queue.FindAsync(id, cancellationToken);
                return request is null ? NotFound(logger, id) : TypedResults.Ok(ServiceRequestDto.From(request, queue.UtcNow));
            })
            .WithName("GetServiceRequest")
            .WithSummary("Get a service request")
            .WithDescription("Returns a single request, including its assignee, SLA due time and whether it's overdue.")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithProblemExample(StatusCodes.Status404NotFound, "Request not found", "No service request exists with ID 1099.");

        requests.MapPost("", async Task<Results<Created<ServiceRequestDto>, ValidationProblem>> (
                CreateServiceRequest body,
                RequestQueue queue,
                CancellationToken cancellationToken) =>
            {
                Dictionary<string, string[]> errors = [];
                var title = Required(body.Title, "title", "Title", ServiceRequest.MaxTitleLength, errors);
                var requestedBy = Required(body.RequestedBy, "requestedBy", "Requested by", ServiceRequest.MaxPersonNameLength, errors);

                if (errors.Count > 0)
                {
                    return Invalid(logger, errors);
                }

                var request = await queue.RaiseAsync(title, requestedBy, body.Category, body.Priority, cancellationToken);
                return TypedResults.Created($"/api/requests/{request.Id}", ServiceRequestDto.From(request, queue.UtcNow));
            })
            .WithName("RaiseServiceRequest")
            .WithSummary("Raise a service request")
            .WithDescription("""
                Adds a `New` request to the queue. The SLA due time is set from the priority:
                Critical 4 hours, High 8 hours, Medium 24 hours and Low 72 hours.
                """)
            .WithProblemExample(StatusCodes.Status400BadRequest, "One or more validation errors occurred.", null,
                new JsonObject { ["title"] = new JsonArray("Title is required.") });

        requests.MapPatch("/{id:int}", async Task<Results<Ok<ServiceRequestDto>, ValidationProblem, ProblemHttpResult>> (
                [Description("The request ID.")] int id,
                UpdateServiceRequest body,
                RequestQueue queue,
                CancellationToken cancellationToken) =>
            {
                Activity.Current?.SetTag("servicedesk.request.id", id);

                Dictionary<string, string[]> errors = [];
                var title = body.Title is null ? null : Required(body.Title, "title", "Title", ServiceRequest.MaxTitleLength, errors);
                var assignee = body.AssignedTo?.Trim();

                if (assignee?.Length > ServiceRequest.MaxPersonNameLength)
                {
                    errors["assignedTo"] = [$"Assigned to must be {ServiceRequest.MaxPersonNameLength} characters or fewer."];
                }

                if (errors.Count > 0)
                {
                    return Invalid(logger, errors);
                }

                var request = await queue.FindAsync(id, cancellationToken);
                if (request is null)
                {
                    return NotFound(logger, id);
                }

                if ((body.Status ?? request.Status) == RequestStatus.InProgress && string.IsNullOrEmpty(assignee ?? request.AssignedTo))
                {
                    return Invalid(logger, "assignedTo", "Assign the request to an agent before moving it to InProgress.");
                }

                if (await queue.UpdateAsync(request, body with { Title = title, AssignedTo = assignee }, cancellationToken) is { } conflict)
                {
                    return TypedResults.Problem(title: "Invalid status change", detail: conflict, statusCode: StatusCodes.Status409Conflict);
                }

                return TypedResults.Ok(ServiceRequestDto.From(request, queue.UtcNow));
            })
            .WithName("UpdateServiceRequest")
            .WithSummary("Update or move a service request")
            .WithDescription("""
                Changes the title, priority or assignee, and moves the request through its lifecycle:
                pick it up (`New` → `InProgress`, which needs an assignee), resolve it (`InProgress` → `Resolved`),
                release it back to the queue (`InProgress` → `New`) or reopen it (`Resolved` → `InProgress`).
                Any other move, such as resolving a request nobody has picked up, returns `409 Conflict`.
                Resolving records the resolution time and whether the request met its SLA.
                """)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithProblemExample(StatusCodes.Status400BadRequest, "One or more validation errors occurred.", null,
                new JsonObject { ["assignedTo"] = new JsonArray("Assign the request to an agent before moving it to InProgress.") })
            .WithProblemExample(StatusCodes.Status404NotFound, "Request not found", "No service request exists with ID 1099.")
            .WithProblemExample(StatusCodes.Status409Conflict, "Invalid status change",
                "Request SR-1002 hasn't been picked up yet. Assign it and move it to InProgress before resolving it.");

        requests.MapDelete("/{id:int}", async Task<Results<NoContent, ProblemHttpResult>> (
                [Description("The request ID.")] int id,
                RequestQueue queue,
                CancellationToken cancellationToken) =>
            {
                Activity.Current?.SetTag("servicedesk.request.id", id);

                var request = await queue.FindAsync(id, cancellationToken);
                if (request is null)
                {
                    return NotFound(logger, id);
                }

                await queue.DeleteAsync(request, cancellationToken);
                return TypedResults.NoContent();
            })
            .WithName("DeleteServiceRequest")
            .WithSummary("Delete a service request")
            .WithDescription("Permanently removes a request, for example one raised by mistake.")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithProblemExample(StatusCodes.Status404NotFound, "Request not found", "No service request exists with ID 1099.");

        app.MapGet("/api/queue/summary", async (RequestQueue queue, CancellationToken cancellationToken) =>
                TypedResults.Ok(await queue.SummarizeAsync(cancellationToken)))
            .WithTags("Queue")
            .WithName("GetQueueSummary")
            .WithSummary("Get the queue summary")
            .WithDescription("Counts open, unassigned, in-progress, critical and overdue requests, plus requests resolved in the last 24 hours.");

        return app;
    }

    // Development-only operations behind the AppHost's dashboard commands.
    public static WebApplication MapDemoEndpoints(this WebApplication app)
    {
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(ServiceRequestEndpoints).FullName!);

        var demo = app.MapGroup("/api/demo").WithTags("Demo");

        demo.MapPost("/seed", async (QueueSimulator simulator, CancellationToken cancellationToken) =>
                TypedResults.Ok(await simulator.SeedAsync(cancellationToken)))
            .WithName("SeedSampleData")
            .WithSummary("Seed sample data")
            .WithDescription("Development only. Replaces every request with 17 realistic IT requests: open, in progress, overdue and recently resolved.");

        demo.MapPost("/simulate", async Task<Results<Ok<SimulationResult>, ValidationProblem>> (
                QueueSimulator simulator,
                CancellationToken cancellationToken,
                [Description("Number of rounds to simulate (1-20).")][Range(1, 20)] int rounds = 3) =>
            {
                if (rounds is < 1 or > 20)
                {
                    return Invalid(logger, "rounds", "Rounds must be between 1 and 20.");
                }

                return TypedResults.Ok(await simulator.SimulateAsync(rounds, cancellationToken));
            })
            .WithName("SimulateTraffic")
            .WithSummary("Simulate service desk traffic")
            .WithDescription("""
                Development only. Each round two employees raise requests, agents pick up and resolve the most urgent ones,
                and now and then a resolved request is reopened. Every call also tries to resolve a request nobody has
                picked up, which the queue rejects, so traces, metrics and structured logs fill up quickly.
                """);

        return app;
    }

    private static string Required(string? value, string field, string label, int maxLength, Dictionary<string, string[]> errors)
    {
        var trimmed = value?.Trim() ?? string.Empty;

        if (trimmed.Length == 0)
        {
            errors[field] = [$"{label} is required."];
        }
        else if (trimmed.Length > maxLength)
        {
            errors[field] = [$"{label} must be {maxLength} characters or fewer."];
        }

        return trimmed;
    }

    private static ValidationProblem Invalid(ILogger logger, string field, string message) =>
        Invalid(logger, new Dictionary<string, string[]> { [field] = [message] });

    private static ValidationProblem Invalid(ILogger logger, Dictionary<string, string[]> errors)
    {
        logger.ValidationFailed(string.Join(" ", errors.Values.SelectMany(e => e)));
        return TypedResults.ValidationProblem(errors);
    }

    private static ProblemHttpResult NotFound(ILogger logger, int id)
    {
        logger.RequestNotFound(id);
        return TypedResults.Problem(
            title: "Request not found",
            detail: $"No service request exists with ID {id}.",
            statusCode: StatusCodes.Status404NotFound);
    }

    // Adds an application/problem+json example to a documented error response.
    private static RouteHandlerBuilder WithProblemExample(
        this RouteHandlerBuilder builder, int statusCode, string title, string? detail, JsonObject? errors = null) =>
        builder.AddOpenApiOperationTransformer((operation, _, _) =>
        {
            if (operation.Responses?.TryGetValue(statusCode.ToString(), out var response) == true
                && response.Content?.TryGetValue("application/problem+json", out var content) == true)
            {
                var example = new JsonObject
                {
                    ["type"] = statusCode switch
                    {
                        StatusCodes.Status400BadRequest => "https://tools.ietf.org/html/rfc9110#section-15.5.1",
                        StatusCodes.Status404NotFound => "https://tools.ietf.org/html/rfc9110#section-15.5.5",
                        _ => "https://tools.ietf.org/html/rfc9110#section-15.5.10"
                    },
                    ["title"] = title,
                    ["status"] = statusCode
                };

                if (detail is not null)
                {
                    example["detail"] = detail;
                }

                if (errors is not null)
                {
                    example["errors"] = errors.DeepClone();
                }

                content.Example = example;
            }

            return Task.CompletedTask;
        });
}