namespace NotificationService;

internal static partial class Log
{
    [LoggerMessage(EventId = 1000, Level = LogLevel.Information,
        Message = "Sent {Template} email {MessageId} to {Recipient} in {ElapsedMilliseconds:F1} ms")]
    public static partial void EmailSent(this ILogger logger, string template, string messageId, string recipient, double elapsedMilliseconds);

    [LoggerMessage(EventId = 1001, Level = LogLevel.Error,
        Message = "Failed to send {Template} email to {Recipient}: {ErrorType}")]
    public static partial void EmailSendFailed(this ILogger logger, Exception exception, string template, string recipient, string errorType);

    [LoggerMessage(EventId = 1002, Level = LogLevel.Information,
        Message = "Issued password reset link for {Recipient}, valid until {ExpiresAt:u}")]
    public static partial void PasswordResetIssued(this ILogger logger, string recipient, DateTimeOffset expiresAt);

    [LoggerMessage(EventId = 1003, Level = LogLevel.Information,
        Message = "Sample email batch finished: {Sent} sent, {Failed} failed")]
    public static partial void SampleBatchSent(this ILogger logger, int sent, int failed);
}
