namespace Notes.Api;

internal static partial class Log
{
    [LoggerMessage(1001, LogLevel.Information, "Listed {NoteCount} notes")]
    public static partial void NotesListed(this ILogger logger, int noteCount);

    [LoggerMessage(1002, LogLevel.Information, "Read note {NoteId} ({TextLength} characters)")]
    public static partial void NoteRead(this ILogger logger, string noteId, int textLength);

    [LoggerMessage(1003, LogLevel.Information, "Created note {NoteId} ({TextLength} characters)")]
    public static partial void NoteCreated(this ILogger logger, string noteId, int textLength);

    [LoggerMessage(1004, LogLevel.Information, "Replaced note {NoteId} ({TextLength} characters)")]
    public static partial void NoteReplaced(this ILogger logger, string noteId, int textLength);

    [LoggerMessage(1005, LogLevel.Information, "Deleted note {NoteId}")]
    public static partial void NoteDeleted(this ILogger logger, string noteId);

    [LoggerMessage(1006, LogLevel.Warning, "Note {NoteId} was not found")]
    public static partial void NoteNotFound(this ILogger logger, string noteId);

    [LoggerMessage(1007, LogLevel.Warning, "Rejected {Operation} request: {ValidationError}")]
    public static partial void RequestRejected(this ILogger logger, string operation, string validationError);

    [LoggerMessage(1008, LogLevel.Information, "Seeded {SampleCount} sample notes ({CreatedCount} created, {ReplacedCount} replaced); {NoteCount} notes stored")]
    public static partial void NotesSeeded(this ILogger logger, int sampleCount, int createdCount, int replacedCount, int noteCount);
}
