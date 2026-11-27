namespace AspireShop.CatalogDbManager;

internal static partial class Log
{
    [LoggerMessage(EventId = 4001, Level = LogLevel.Information,
        Message = "Catalog database initialized in {ElapsedMilliseconds} ms")]
    public static partial void DatabaseInitialized(this ILogger logger, long elapsedMilliseconds);

    [LoggerMessage(EventId = 4002, Level = LogLevel.Information,
        Message = "Seeded {SeededCount} catalog {SeededEntity}")]
    public static partial void CatalogDataSeeded(this ILogger logger, int seededCount, string seededEntity);

    [LoggerMessage(EventId = 4003, Level = LogLevel.Warning,
        Message = "Reverted all catalog migrations; the catalog is re-created when the service restarts")]
    public static partial void MigrationsReverted(this ILogger logger);
}
