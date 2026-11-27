using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpenTelemetry;
using OpenTelemetry.Context.Propagation;
using RabbitMQ.Client;

namespace WorkerCSharp;

// The message contract shared with the API and the other workers (camelCase JSON).
internal sealed record TaskMessage(string? TaskId, string? Type, string? Name, string? Data);

internal sealed record StatusMessage(string TaskId, string Status, string Worker, DateTimeOffset StartedAt);

internal sealed record ResultMessage(string TaskId, string Status, string Worker, DateTimeOffset CompletedAt, long DurationMs, TaskResult? Result, TaskError? Error);

// The result envelope every worker returns: a headline, display-ready facts and the raw details.
internal sealed record TaskResult(string Headline, IReadOnlyList<Fact> Facts, object Details);

internal sealed record Fact(string Label, string Value);

internal sealed record TaskError(string Type, string Message);

// Publishing and trace context propagation over RabbitMQ, with the OpenTelemetry messaging
// semantic-convention tags. Messages use the default exchange with the queue name as the
// routing key, so the destination is the routing key alone.
internal static class Messaging
{
    public const string StatusQueue = "task_status";
    public const string ResultsQueue = "results";

    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly TextMapPropagator Propagator = Propagators.DefaultTextMapPropagator;

    public static void SetTags(Activity? activity, string operation, string queue, AmqpTcpEndpoint broker, string? messageId, string? taskId, int bodySize)
    {
        if (activity is null)
        {
            return;
        }

        activity.SetTag("messaging.system", "rabbitmq");
        activity.SetTag("messaging.operation.type", operation);
        activity.SetTag("messaging.operation.name", operation == "send" ? "publish" : "process");
        activity.SetTag("messaging.destination.name", queue);
        activity.SetTag("messaging.rabbitmq.destination.routing_key", queue);
        activity.SetTag("messaging.message.id", messageId);
        activity.SetTag("messaging.message.conversation_id", taskId);
        activity.SetTag("messaging.message.body.size", bodySize);
        activity.SetTag("server.address", broker.HostName);
        activity.SetTag("server.port", broker.Port);
    }

    // AMQP header values arrive as byte arrays.
    public static PropagationContext ExtractContext(IReadOnlyBasicProperties properties) =>
        Propagator.Extract(default, properties.Headers, static (headers, key) =>
            headers is not null && headers.TryGetValue(key, out var value) && value is not null
                ? [value is byte[] bytes ? Encoding.UTF8.GetString(bytes) : value.ToString() ?? string.Empty]
                : []);

    // Publishes under a PRODUCER span whose context is injected into the message headers, so the
    // API's consumer span becomes its child. The channel has publisher confirms on, so this
    // completes once the broker has accepted the message.
    public static async Task PublishAsync<T>(IChannel channel, AmqpTcpEndpoint broker, string queue, string taskId, T message, CancellationToken cancellationToken)
    {
        var body = JsonSerializer.SerializeToUtf8Bytes(message, JsonOptions);
        var messageId = Guid.NewGuid().ToString("N");

        using var activity = WorkerTelemetry.ActivitySource.StartActivity($"publish {queue}", ActivityKind.Producer);
        SetTags(activity, "send", queue, broker, messageId, taskId, body.Length);

        var headers = new Dictionary<string, object?>();
        Propagator.Inject(new PropagationContext(activity?.Context ?? default, Baggage.Current), headers, static (carrier, key, value) => carrier[key] = value);

        var properties = new BasicProperties
        {
            Persistent = true,
            ContentType = "application/json",
            MessageId = messageId,
            CorrelationId = taskId,
            Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds()),
            Headers = headers
        };

        try
        {
            await channel.BasicPublishAsync(string.Empty, queue, mandatory: false, properties, body, cancellationToken);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.SetTag("error.type", ex.GetType().FullName);
            activity?.AddException(ex);
            throw;
        }
    }
}
