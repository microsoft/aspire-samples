using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace WorkerCSharp;

// Consumes tasks.report and turns meeting notes into structured reports. Each task continues the
// trace the API started: process tasks.report -> generate report -> publish task_status and
// publish results, which the API consumes.
internal sealed partial class ReportWorker(IConnection connection, WorkerTelemetry telemetry, ILogger<ReportWorker> logger) : BackgroundService
{
    private const string WorkerName = "worker-csharp";
    private const string TaskType = "report";
    private const string Queue = "tasks.report";
    private const ushort Concurrency = 4;
    private const int MaxDataLength = 10_000;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await using var publisher = await connection.CreateChannelAsync(
            new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true),
            stoppingToken);
        await using var consumer = await connection.CreateChannelAsync(
            new CreateChannelOptions(publisherConfirmationsEnabled: false, publisherConfirmationTrackingEnabled: false, consumerDispatchConcurrency: Concurrency),
            stoppingToken);

        foreach (var queue in new[] { Queue, Messaging.StatusQueue, Messaging.ResultsQueue })
        {
            await consumer.QueueDeclareAsync(queue, durable: true, exclusive: false, autoDelete: false, cancellationToken: stoppingToken);
        }

        await consumer.BasicQosAsync(prefetchSize: 0, prefetchCount: Concurrency, global: false, stoppingToken);

        var deliveries = new AsyncEventingBasicConsumer(consumer);
        deliveries.ReceivedAsync += (_, delivery) => OnMessageAsync(consumer, publisher, delivery, stoppingToken);
        await consumer.BasicConsumeAsync(Queue, autoAck: false, deliveries, stoppingToken);

        logger.WorkerStarted(Queue, Concurrency);

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    // Handles one delivery under a CONSUMER span parented to the API's publish span.
    private async Task OnMessageAsync(IChannel consumer, IChannel publisher, BasicDeliverEventArgs delivery, CancellationToken cancellationToken)
    {
        var parent = Messaging.ExtractContext(delivery.BasicProperties);
        using var activity = WorkerTelemetry.ActivitySource.StartActivity($"process {Queue}", ActivityKind.Consumer, parent.ActivityContext);
        Messaging.SetTags(activity, "process", Queue, connection.Endpoint, delivery.BasicProperties.MessageId, delivery.BasicProperties.CorrelationId, delivery.Body.Length);
        activity?.SetTag("messaging.rabbitmq.message.delivery_tag", (long)delivery.DeliveryTag);

        try
        {
            var task = JsonSerializer.Deserialize<TaskMessage>(delivery.Body.Span, Messaging.JsonOptions);
            await HandleTaskAsync(publisher, task, activity, cancellationToken);
            await consumer.BasicAckAsync(delivery.DeliveryTag, multiple: false, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Drop messages that can't be handled instead of redelivering them forever.
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.SetTag("error.type", ex.GetType().Name);
            activity?.AddException(ex);
            logger.MessageDropped(ex, Queue);
            await consumer.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: false, cancellationToken);
        }
    }

    private async Task HandleTaskAsync(IChannel publisher, TaskMessage? task, Activity? activity, CancellationToken cancellationToken)
    {
        if (task?.TaskId is not { } taskId || !TaskIdFormat().IsMatch(taskId))
        {
            throw new InvalidDataException("The message has no valid taskId.");
        }

        var name = string.IsNullOrWhiteSpace(task.Name) ? "notes.md" : task.Name;
        activity?.SetTag("taskqueue.task.id", taskId);
        activity?.SetTag("taskqueue.task.type", TaskType);
        activity?.SetTag("taskqueue.document.name", name);

        var started = Stopwatch.GetTimestamp();
        telemetry.TaskStarted(TaskType);
        await Messaging.PublishAsync(publisher, connection.Endpoint, Messaging.StatusQueue, taskId,
            new StatusMessage(taskId, "processing", WorkerName, DateTimeOffset.UtcNow), cancellationToken);

        TaskResult? result = null;
        TaskError? error = null;
        try
        {
            result = await GenerateReportAsync(taskId, name, Validate(task), cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            error = new TaskError(ex.GetType().Name, ex.Message);
            activity?.AddEvent(new ActivityEvent("task.failed", tags: new ActivityTagsCollection { ["error.type"] = error.Type }));
            if (ex is InvalidDataException)
            {
                logger.ReportRejected(taskId, name, ex.Message);
            }
            else
            {
                logger.ReportFailed(ex, taskId, name);
            }
        }

        var elapsed = Stopwatch.GetElapsedTime(started);
        var outcome = error is null ? "completed" : "failed";
        telemetry.TaskFinished(TaskType, outcome, elapsed, error?.Type);

        await Messaging.PublishAsync(publisher, connection.Endpoint, Messaging.ResultsQueue, taskId,
            new ResultMessage(taskId, outcome, WorkerName, DateTimeOffset.UtcNow, (long)elapsed.TotalMilliseconds, result, error), cancellationToken);
    }

    private static string Validate(TaskMessage task) => task switch
    {
        { Type: not TaskType } => throw new InvalidDataException($"Unsupported task type '{task.Type}'; this worker handles '{TaskType}'."),
        { Data: null or "" } => throw new InvalidDataException("The document is empty."),
        { Data.Length: > MaxDataLength } => throw new InvalidDataException($"The document is longer than {MaxDataLength} characters."),
        _ => task.Data!
    };

    // The domain span: loads the notes (simulated latency) and builds the report.
    private async Task<TaskResult> GenerateReportAsync(string taskId, string name, string notes, CancellationToken cancellationToken)
    {
        using var activity = WorkerTelemetry.ActivitySource.StartActivity("generate report");
        activity?.SetTag("taskqueue.task.id", taskId);
        activity?.SetTag("taskqueue.document.name", name);

        try
        {
            // Stands in for loading the source document and rendering the report to PDF.
            await Task.Delay(Random.Shared.Next(150, 450), cancellationToken);

            var report = ReportGenerator.Generate(notes);
            activity?.AddEvent(new ActivityEvent("report.parsed", tags: new ActivityTagsCollection
            {
                ["taskqueue.report.sections"] = report.Sections.Count,
                ["taskqueue.report.words"] = report.WordCount
            }));
            activity?.SetTag("taskqueue.report.title", report.Title);
            activity?.SetTag("taskqueue.report.sections", report.Sections.Count);
            activity?.SetTag("taskqueue.report.words", report.WordCount);
            activity?.SetTag("taskqueue.report.action_items", report.ActionItems.Count);
            telemetry.ReportGenerated(report.WordCount);

            var result = report.ToResult();
            activity?.AddEvent(new ActivityEvent("report.rendered"));
            logger.ReportGenerated(taskId, name, report.Title, report.Sections.Count, report.WordCount, report.ActionItems.Count);
            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.SetTag("error.type", ex.GetType().Name);
            activity?.AddException(ex);
            throw;
        }
    }

    [GeneratedRegex("^task-[0-9a-f]{8}$")]
    private static partial Regex TaskIdFormat();
}
