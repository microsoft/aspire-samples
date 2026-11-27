namespace AspireShop.CatalogService;

internal static partial class Log
{
    [LoggerMessage(EventId = 1001, Level = LogLevel.Information,
        Message = "Served catalog page with {ItemCount} items (filter {CatalogFilter}, direction {PageDirection}, last page {IsLastPage})")]
    public static partial void CatalogPageServed(this ILogger logger, int itemCount, string catalogFilter, string pageDirection, bool isLastPage);

    [LoggerMessage(EventId = 1002, Level = LogLevel.Warning,
        Message = "Rejected catalog page request: {ValidationError} (before {Before}, after {After}, page size {PageSize})")]
    public static partial void InvalidPageRequest(this ILogger logger, string validationError, int? before, int? after, int pageSize);

    [LoggerMessage(EventId = 1003, Level = LogLevel.Warning,
        Message = "Catalog item {CatalogItemId} was not found")]
    public static partial void CatalogItemNotFound(this ILogger logger, int catalogItemId);

    [LoggerMessage(EventId = 1004, Level = LogLevel.Warning,
        Message = "Catalog item {CatalogItemId} has no image file {PictureFileName}")]
    public static partial void CatalogImageMissing(this ILogger logger, int catalogItemId, string pictureFileName);
}
