using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using OpenTelemetry.Context.Propagation;
using OpenTelemetry;
using OpenTelemetry.Trace;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();

builder.AddRabbitMQClient("messaging");

builder.Services.AddHostedService<ReportWorker>();

var host = builder.Build();
host.Run();

class ReportWorker(IConnection connection, ILogger<ReportWorker> logger) : BackgroundService
{
    private const string TasksQueue = "tasks";
    private const string ResultsQueue = "results";
    private const string TaskStatusQueue = "task_status";
    private const string WorkerName = "csharp-worker";

    // ActivitySource for distributed tracing
    private static readonly ActivitySource ActivitySource = new("TaskQueue.Worker.CSharp", "1.0.0");
    private static readonly TextMapPropagator Propagator = Propagators.DefaultTextMapPropagator;

    // Use JsonSerializerDefaults.Web for consistent camelCase JSON serialization
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private async Task PublishTaskStatusAsync(IChannel channel, string taskId, string status, object? additionalData = null, CancellationToken cancellationToken = default)
    {
        using var activity = ActivitySource.StartActivity("rabbitmq.publish task_status", ActivityKind.Producer);
        try
        {
            // Messaging semantic conventions
            activity?.SetTag("messaging.system", "rabbitmq");
            activity?.SetTag("messaging.destination.name", TaskStatusQueue);
            activity?.SetTag("messaging.operation", "publish");
            activity?.SetTag("task.id", taskId);
            activity?.SetTag("task.status", status);

            var statusMessage = new
            {
                TaskId = taskId,
                Status = status,
                Worker = WorkerName,
                Timestamp = DateTime.Now,
                AdditionalData = additionalData
            };

            var statusJson = JsonSerializer.Serialize(statusMessage, JsonOptions);
            var statusBody = Encoding.UTF8.GetBytes(statusJson);

            // Inject trace context into message headers
            var properties = new BasicProperties();
            var headers = new Dictionary<string, object?>();
            Propagator.Inject(new PropagationContext(activity?.Context ?? default, Baggage.Current), headers, (dict, key, value) =>
            {
                dict[key] = value;
            });
            properties.Headers = headers;

            await channel.BasicPublishAsync(
                exchange: string.Empty,
                routingKey: TaskStatusQueue,
                mandatory: false,
                basicProperties: properties,
                body: statusBody,
                cancellationToken: cancellationToken);

            logger.LogInformation("[{Time}] Status update published: {TaskId} -> {Status}", DateTime.Now, taskId, status);
            activity?.SetStatus(ActivityStatusCode.Ok);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[{Time}] Error publishing status for task {TaskId}", DateTime.Now, taskId);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.RecordException(ex);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("[{Time}] C# worker starting...", DateTime.Now);

        var channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);

        // Declare queues
        await channel.QueueDeclareAsync(
            queue: TasksQueue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null,
            cancellationToken: stoppingToken);

        await channel.QueueDeclareAsync(
            queue: ResultsQueue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null,
            cancellationToken: stoppingToken);

        await channel.QueueDeclareAsync(
            queue: TaskStatusQueue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null,
            cancellationToken: stoppingToken);

        // Set prefetch count
        await channel.BasicQosAsync(
            prefetchSize: 0,
            prefetchCount: 1,
            global: false,
            cancellationToken: stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += async (model, ea) =>
        {
            // Extract trace context from message headers
            var parentContext = Propagator.Extract(default, ea.BasicProperties.Headers, (headers, key) =>
            {
                if (headers != null && headers.TryGetValue(key, out var value))
                {
                    return value is byte[] bytes ? [Encoding.UTF8.GetString(bytes)] : [value?.ToString() ?? string.Empty];
                }
                return [];
            });

            // Start activity linked to parent context
            using var activity = ActivitySource.StartActivity(
                "rabbitmq.process task",
                ActivityKind.Consumer,
                parentContext.ActivityContext);

            try
            {
                // Messaging semantic conventions
                activity?.SetTag("messaging.system", "rabbitmq");
                activity?.SetTag("messaging.source.name", TasksQueue);
                activity?.SetTag("messaging.operation", "process");

                var messageBody = Encoding.UTF8.GetString(ea.Body.Span);
                logger.LogInformation("[{Time}] Received message: {Message}", DateTime.Now, messageBody);

                var task = JsonSerializer.Deserialize<TaskMessage>(messageBody, JsonOptions);

                if (task is null)
                {
                    logger.LogWarning("[{Time}] Received invalid task message", DateTime.Now);
                    await channel.BasicAckAsync(ea.DeliveryTag, false, stoppingToken);
                    activity?.SetStatus(ActivityStatusCode.Error, "Invalid task message");
                    return;
                }

                activity?.SetTag("task.id", task.TaskId);
                activity?.SetTag("task.type", task.Type);
                activity?.SetTag("messaging.message.id", task.TaskId);

                logger.LogInformation("[{Time}] Processing task {TaskId} (type: {Type})",
                    DateTime.Now, task.TaskId, task.Type);

                // Publish processing status
                await PublishTaskStatusAsync(channel, task.TaskId!, "processing", cancellationToken: stoppingToken);

                // Only process 'report' tasks
                if (task.Type != "report")
                {
                    logger.LogInformation("[{Time}] Skipping task {TaskId} - not a report task",
                        DateTime.Now, task.TaskId);

                    await PublishTaskStatusAsync(channel, task.TaskId!, "skipped",
                        new { reason = "not a report task" }, stoppingToken);

                    await channel.BasicAckAsync(ea.DeliveryTag, false, stoppingToken);
                    activity?.AddEvent(new ActivityEvent("task.skipped", tags: new ActivityTagsCollection { { "reason", "not a report task" } }));
                    activity?.SetStatus(ActivityStatusCode.Ok);
                    return;
                }

                // Process the task with a child activity
                using (var processActivity = ActivitySource.StartActivity("task.process_report"))
                {
                    processActivity?.SetTag("task.id", task.TaskId);
                    var result = await ProcessReportTask(task);
                    processActivity?.SetStatus(ActivityStatusCode.Ok);

                    // Send result back with trace context
                    using (var publishActivity = ActivitySource.StartActivity("rabbitmq.publish results", ActivityKind.Producer))
                    {
                        publishActivity?.SetTag("messaging.system", "rabbitmq");
                        publishActivity?.SetTag("messaging.destination.name", ResultsQueue);
                        publishActivity?.SetTag("messaging.operation", "publish");
                        publishActivity?.SetTag("task.id", task.TaskId);

                        var resultMessage = new ResultMessage
                        {
                            TaskId = task.TaskId,
                            Worker = WorkerName,
                            Result = result,
                            CompletedAt = DateTime.Now
                        };

                        var resultJson = JsonSerializer.Serialize(resultMessage, JsonOptions);
                        var resultBody = Encoding.UTF8.GetBytes(resultJson);

                        // Inject trace context into result message
                        var resultProperties = new BasicProperties();
                        var resultHeaders = new Dictionary<string, object?>();
                        Propagator.Inject(new PropagationContext(publishActivity?.Context ?? default, Baggage.Current), resultHeaders, (dict, key, value) =>
                        {
                            dict[key] = value;
                        });
                        resultProperties.Headers = resultHeaders;

                        await channel.BasicPublishAsync(
                            exchange: string.Empty,
                            routingKey: ResultsQueue,
                            mandatory: false,
                            basicProperties: resultProperties,
                            body: resultBody,
                            cancellationToken: stoppingToken);

                        publishActivity?.SetStatus(ActivityStatusCode.Ok);
                    }
                }

                logger.LogInformation("[{Time}] Completed task {TaskId}", DateTime.Now, task.TaskId);
                activity?.AddEvent(new ActivityEvent("task.completed"));
                activity?.SetStatus(ActivityStatusCode.Ok);

                await channel.BasicAckAsync(ea.DeliveryTag, false, stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[{Time}] Error processing message", DateTime.Now);
                
                // Try to publish error status if we have task info
                try
                {
                    var messageBody = Encoding.UTF8.GetString(ea.Body.Span);
                    var task = JsonSerializer.Deserialize<TaskMessage>(messageBody, JsonOptions);
                    if (task?.TaskId != null)
                    {
                        await PublishTaskStatusAsync(channel, task.TaskId, "error", 
                            new { error = ex.Message }, stoppingToken);
                    }
                }
                catch
                {
                    // Ignore errors in error handling
                }
                
                await channel.BasicNackAsync(ea.DeliveryTag, false, true, stoppingToken);
            }
        };

        await channel.BasicConsumeAsync(
            queue: TasksQueue,
            autoAck: false,
            consumer: consumer,
            cancellationToken: stoppingToken);

        logger.LogInformation("[{Time}] C# worker started. Waiting for tasks...", DateTime.Now);

        // Keep the worker running
        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    private async Task<object> ProcessReportTask(TaskMessage task)
    {
        await Task.Delay(100); // Simulate processing time

        try
        {
            var data = task.Data ?? string.Empty;
            var lines = data.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            // Generate a structured report
            var report = new
            {
                Title = $"Report generated at {DateTime.Now:yyyy-MM-dd HH:mm:ss}",
                Summary = new
                {
                    TotalLines = lines.Length,
                    TotalCharacters = data.Length,
                    AverageLineLength = lines.Length > 0 ? data.Length / lines.Length : 0,
                    ProcessedBy = WorkerName,
                    ProcessedAt = DateTime.Now
                },
                Content = new
                {
                    Lines = lines.Take(10).Select((line, index) => new
                    {
                        LineNumber = index + 1,
                        Content = line,
                        Length = line.Length,
                        WordCount = line.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length
                    }).ToArray()
                },
                Statistics = new
                {
                    ShortestLine = lines.Any() ? lines.MinBy(l => l.Length)?.Length ?? 0 : 0,
                    LongestLine = lines.Any() ? lines.MaxBy(l => l.Length)?.Length ?? 0 : 0,
                    TotalWords = lines.Sum(l => l.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length)
                },
                Metadata = new
                {
                    Format = "Structured Report",
                    Version = "1.0",
                    Generator = "C# Report Worker",
                    Timestamp = DateTime.Now
                }
            };

            return report;
        }
        catch (Exception ex)
        {
            return new
            {
                Error = ex.Message,
                ErrorType = ex.GetType().Name
            };
        }
    }
}

record TaskMessage
{
    public string? TaskId { get; init; }
    public string? Type { get; init; }
    public string? Data { get; init; }
}

record ResultMessage
{
    public string? TaskId { get; init; }
    public string? Worker { get; init; }
    public object? Result { get; init; }
    public DateTime CompletedAt { get; init; }
}
