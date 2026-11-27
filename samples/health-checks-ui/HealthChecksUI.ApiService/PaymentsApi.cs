using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Globalization;
using Microsoft.AspNetCore.Http.HttpResults;

namespace HealthChecksUI.ApiService;

public static class PaymentsApi
{
    public const int DefaultLimit = 20;
    public const int MaxLimit = 50;

    private static readonly decimal s_minAmount = decimal.Parse(PaymentRequest.MinAmount, CultureInfo.InvariantCulture);
    private static readonly decimal s_maxAmount = decimal.Parse(PaymentRequest.MaxAmount, CultureInfo.InvariantCulture);

    public static IEndpointRouteBuilder MapPaymentsApi(this IEndpointRouteBuilder routes)
    {
        var payments = routes.MapGroup("/api/payments").WithTags("Payments");

        payments.MapPost("/", async Task<Results<Created<Payment>, ValidationProblem, ProblemHttpResult>> (
                PaymentRequest request, PaymentGateway gateway, CancellationToken cancellationToken) =>
            {
                if (Validate(request) is { } errors)
                {
                    return TypedResults.ValidationProblem(errors, title: "Invalid payment request");
                }

                try
                {
                    var payment = await gateway.AuthorizeAsync(request, Merchants.Find(request.MerchantId)!, cancellationToken);
                    return TypedResults.Created($"/api/payments/{payment.Id}", payment);
                }
                catch (DependencyUnavailableException ex)
                {
                    return TypedResults.Problem(
                        title: $"{ex.Dependency.DisplayName} unavailable",
                        detail: $"{ex.Message} {ex.Dependency.ImpactWhenDown} Retry the payment later.",
                        statusCode: StatusCodes.Status503ServiceUnavailable,
                        extensions: new Dictionary<string, object?> { ["dependency"] = ex.Dependency.Name });
                }
            })
            .WithName("AuthorizePayment")
            .WithSummary("Authorize a card payment")
            .WithDescription("""
                Scores the payment for fraud, converts it to USD, asks the card processor to authorize it, and records it in the payment store.
                Returns `201 Created` for approved **and** declined payments; check `status` and `declineReason`.

                Fraud scoring and FX rates are non-critical: when they're unavailable the gateway falls back to static fraud rules or
                the last cached FX rate and lists the fallback in `fallbacks`. When the card processor or the payment store is
                unavailable the payment fails with a `503` problem.
                """)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        payments.MapGet("/", async Task<Results<Ok<IReadOnlyList<Payment>>, ValidationProblem>> (
                PaymentStore store,
                [Description("The maximum number of payments to return (1-50).")][Range(1, MaxLimit)] int limit = DefaultLimit) =>
            {
                if (limit is < 1 or > MaxLimit)
                {
                    return TypedResults.ValidationProblem(
                        new Dictionary<string, string[]> { [nameof(limit)] = [$"'{nameof(limit)}' must be between 1 and {MaxLimit}."] },
                        title: "Invalid limit");
                }

                return TypedResults.Ok(await store.GetRecentAsync(limit));
            })
            .WithName("ListRecentPayments")
            .WithSummary("List recent payments")
            .WithDescription("Returns the most recently processed payments, newest first. Payments are kept for 24 hours.");

        payments.MapGet("/{paymentId}", async Task<Results<Ok<Payment>, ProblemHttpResult>> (
                [Description("The payment ID, e.g. `pay_3f9a1c7e5b2d4f60`.")] string paymentId,
                PaymentStore store) =>
            {
                Activity.Current?.SetTag("contoso.payment.id", paymentId);

                return await store.GetAsync(paymentId) is { } payment
                    ? TypedResults.Ok(payment)
                    : TypedResults.Problem(
                        title: "Payment not found",
                        detail: $"No payment with ID '{paymentId}' was processed in the last 24 hours.",
                        statusCode: StatusCodes.Status404NotFound);
            })
            .WithName("GetPayment")
            .WithSummary("Get a payment")
            .WithDescription("Returns a payment processed in the last 24 hours, including its fraud score, FX rate, and any fallbacks used.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        routes.MapGet("/api/merchants", () => TypedResults.Ok(Merchants.All))
            .WithTags("Payments")
            .WithName("ListMerchants")
            .WithSummary("List merchants")
            .WithDescription("Returns the merchants that can take payments through the gateway.");

        routes.MapGet("/api/status", (GatewayStatusMonitor monitor) => TypedResults.Ok(monitor.GetStatus()))
            .WithTags("Status")
            .WithName("GetGatewayStatus")
            .WithSummary("Get the gateway status")
            .WithDescription($"""
                Returns the status of the gateway's dependencies for a public status page. A background health check publisher
                checks them every {GatewayStatusMonitor.CheckInterval.TotalSeconds} seconds and keeps the last {GatewayStatusMonitor.HistoryLength} results.
                Unlike the detailed `/healthz` endpoint that HealthChecksUI polls, this response contains no exception details.
                """);

        return routes;
    }

    /// <summary>Development-only endpoints that back the AppHost's dashboard commands.</summary>
    public static IEndpointRouteBuilder MapDevEndpoints(this IEndpointRouteBuilder routes)
    {
        var dev = routes.MapGroup("/dev").ExcludeFromDescription();

        dev.MapPost("/simulate-traffic", async (PaymentTrafficSimulator simulator, CancellationToken cancellationToken) =>
            TypedResults.Ok(await simulator.RunAsync(payments: 40, cancellationToken)));

        dev.MapPost("/dependencies/{dependency}/{mode}", Results<Ok<DependencyModeChange>, ProblemHttpResult> (
            string dependency, string mode, PaymentDependencies dependencies, ILogger<PaymentDependencies> logger) =>
        {
            if (dependencies.FindSimulated(dependency) is not { } simulated)
            {
                return TypedResults.Problem(
                    title: "Unknown dependency",
                    detail: $"Use one of: {string.Join(", ", dependencies.Simulated.Select(d => d.Name))}.",
                    statusCode: StatusCodes.Status404NotFound);
            }

            if (!Enum.TryParse<DependencyMode>(mode, ignoreCase: true, out var dependencyMode))
            {
                return TypedResults.Problem(
                    title: "Unknown mode",
                    detail: $"Use one of: {string.Join(", ", Enum.GetNames<DependencyMode>())}.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            simulated.Mode = dependencyMode;
            logger.IncidentSimulated(simulated.Name, dependencyMode);
            return TypedResults.Ok(new DependencyModeChange(simulated.Name, dependencyMode));
        });

        dev.MapPost("/dependencies/restore", (PaymentDependencies dependencies, ILogger<PaymentDependencies> logger) =>
        {
            foreach (var dependency in dependencies.Simulated)
            {
                dependency.Mode = DependencyMode.Operational;
            }

            logger.DependenciesRestored(dependencies.Simulated.Count);
            return TypedResults.Ok(dependencies.Simulated.Select(d => new DependencyModeChange(d.Name, d.Mode)));
        });

        return routes;
    }

    private static Dictionary<string, string[]>? Validate(PaymentRequest request)
    {
        var errors = new Dictionary<string, string[]>();

        if (Merchants.Find(request.MerchantId) is null)
        {
            errors["merchantId"] = [$"Unknown merchant '{request.MerchantId}'. Use one of: {string.Join(", ", Merchants.All.Select(m => m.Id))}."];
        }

        if (request.Amount < s_minAmount || request.Amount > s_maxAmount)
        {
            errors["amount"] = [$"The amount must be between {s_minAmount} and {s_maxAmount}."];
        }
        else if (request.Currency == Currency.JPY && decimal.Truncate(request.Amount) != request.Amount)
        {
            errors["amount"] = ["JPY amounts can't have a fractional part."];
        }

        return errors.Count == 0 ? null : errors;
    }
}

public sealed record DependencyModeChange(string Dependency, DependencyMode Mode);
