using Microsoft.Extensions.Logging;

namespace WorkerCSharp;

internal static partial class Log
{
    [LoggerMessage(1, LogLevel.Information, "C# worker consuming {Queue} (up to {Concurrency} tasks at a time)")]
    public static partial void WorkerStarted(this ILogger logger, string queue, int concurrency);

    [LoggerMessage(2, LogLevel.Information, "Generated report \"{ReportTitle}\" for task {TaskId} from {DocumentName}: {SectionCount} sections, {WordCount} words, {ActionItemCount} action items")]
    public static partial void ReportGenerated(this ILogger logger, string taskId, string documentName, string reportTitle, int sectionCount, int wordCount, int actionItemCount);

    [LoggerMessage(3, LogLevel.Warning, "Could not generate a report for task {TaskId} from {DocumentName}: {Reason}")]
    public static partial void ReportRejected(this ILogger logger, string taskId, string documentName, string reason);

    [LoggerMessage(4, LogLevel.Error, "Report generation failed for task {TaskId} ({DocumentName})")]
    public static partial void ReportFailed(this ILogger logger, Exception exception, string taskId, string documentName);

    [LoggerMessage(5, LogLevel.Error, "Dropped a {Queue} message that could not be processed")]
    public static partial void MessageDropped(this ILogger logger, Exception exception, string queue);
}
