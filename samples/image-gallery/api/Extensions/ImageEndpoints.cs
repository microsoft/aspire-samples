using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Antiforgery;
using Azure.Storage.Blobs;
using Azure.Storage.Queues;
using Api.Data;
using Api.Models;
using SkiaSharp;
using System.Text.Json;

namespace Api.Extensions;

public static class ImageEndpoints
{
    private const long MaxFileSizeBytes = 10 * 1024 * 1024; // 10 MB
    // Cap decoded pixel count to guard against decompression bombs:
    // a heavily compressed image well under MaxFileSizeBytes can declare
    // dimensions that decode to multi-gigabyte SKBitmap allocations.
    // 100 MP allows ~10000x10000 images while bounding peak memory at ~400 MB (RGBA).
    private const long MaxPixelCount = 100_000_000;
    private const string ThumbnailContentType = "image/jpeg";
    private static readonly string[] AllowedImageFormats = [".jpg", ".jpeg", ".png", ".gif", ".webp"];
    private static readonly IReadOnlyDictionary<string, SKEncodedImageFormat> AllowedFormatsByExtension =
        new Dictionary<string, SKEncodedImageFormat>(StringComparer.OrdinalIgnoreCase)
        {
            [".jpg"] = SKEncodedImageFormat.Jpeg,
            [".jpeg"] = SKEncodedImageFormat.Jpeg,
            [".png"] = SKEncodedImageFormat.Png,
            [".gif"] = SKEncodedImageFormat.Gif,
            [".webp"] = SKEncodedImageFormat.Webp
        };
    private static readonly IReadOnlyDictionary<SKEncodedImageFormat, string> ContentTypesByFormat =
        new Dictionary<SKEncodedImageFormat, string>
        {
            [SKEncodedImageFormat.Jpeg] = "image/jpeg",
            [SKEncodedImageFormat.Png] = "image/png",
            [SKEncodedImageFormat.Gif] = "image/gif",
            [SKEncodedImageFormat.Webp] = "image/webp"
        };

    public static WebApplication MapImages(this WebApplication app)
    {
        var group = app.MapGroup("/api");

        // Get antiforgery token for client-side requests
        group.MapGet("/antiforgery", (IAntiforgery antiforgery, HttpContext context) =>
        {
            var tokens = antiforgery.GetAndStoreTokens(context);
            return Results.Ok(new { token = tokens.RequestToken });
        });

        // Get all images (with pagination)
        group.MapGet("/images", async (ImageDbContext db, int? skip = null, int? take = null) =>
        {
            var query = db.Images.OrderByDescending(i => i.UploadedAt);

            // Apply pagination with reasonable defaults and limits
            var skipCount = Math.Max(0, skip ?? 0);
            var takeCount = Math.Clamp(take ?? 100, 1, 100); // Max 100 items per page

            var images = await query
                .Skip(skipCount)
                .Take(takeCount)
                .ToListAsync();

            return images.Select(ImageDto.FromImage).ToList();
        });

        // Get image by id
        group.MapGet("/images/{id}", async (int id, ImageDbContext db) =>
        {
            var image = await db.Images.FindAsync(id);
            return image is not null ? Results.Ok(ImageDto.FromImage(image)) : Results.NotFound();
        });

        // Serve image blob
        group.MapGet("/images/{id}/blob", async (
            int id,
            ImageDbContext db,
            BlobContainerClient containerClient) =>
        {
            var image = await db.Images.FindAsync(id);
            if (image is null)
            {
                return Results.NotFound();
            }

            var blobName = image.BlobUrl.Split('/').Last();
            var blobClient = containerClient.GetBlobClient(blobName);
            
            var download = await blobClient.DownloadStreamingAsync();
            return Results.Stream(download.Value.Content, image.ContentType);
        });

        // Serve thumbnail blob
        group.MapGet("/images/{id}/thumbnail", async (
            int id,
            ImageDbContext db,
            BlobContainerClient containerClient) =>
        {
            var image = await db.Images.FindAsync(id);
            if (image is null || image.ThumbnailUrl is null)
            {
                return Results.NotFound();
            }

            var thumbnailName = image.ThumbnailUrl.Split('/').Last();
            var blobClient = containerClient.GetBlobClient(thumbnailName);
            
            var download = await blobClient.DownloadStreamingAsync();
            return Results.Stream(
                download.Value.Content,
                string.IsNullOrWhiteSpace(download.Value.Details.ContentType)
                    ? ThumbnailContentType
                    : download.Value.Details.ContentType);
        });

        // Upload image
        group.MapPost("/images", async (
            IFormFile file,
            IAntiforgery antiforgery,
            HttpContext context,
            ImageDbContext db,
            BlobContainerClient containerClient,
            QueueServiceClient queueService,
            ILogger<Program> logger) =>
        {
            var antiforgeryValidationResult = await ValidateAntiforgeryAsync(antiforgery, context);
            if (antiforgeryValidationResult is not null)
            {
                return antiforgeryValidationResult;
            }

            if (file == null || file.Length == 0)
            {
                return Results.BadRequest(new { error = "No file uploaded" });
            }

            // Validate file size
            if (file.Length > MaxFileSizeBytes)
            {
                return Results.BadRequest(new { error = $"File size exceeds maximum allowed size of {MaxFileSizeBytes / (1024 * 1024)} MB" });
            }

            // Validate content type
            if (!file.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            {
                return Results.BadRequest(new { error = "File must be an image" });
            }

            // Validate file extension
            var fileExtension = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (string.IsNullOrEmpty(fileExtension) || !AllowedFormatsByExtension.ContainsKey(fileExtension))
            {
                return Results.BadRequest(new { error = $"File type not allowed. Allowed types: {string.Join(", ", AllowedImageFormats)}" });
            }

            var validatedContentType = GetValidatedImageContentType(file, fileExtension, context.RequestAborted);
            if (validatedContentType is null)
            {
                return Results.BadRequest(new { error = "File contents must be a valid supported image" });
            }

            try
            {
                // Get container and queue clients
                var queueClient = queueService.GetQueueClient("thumbnails");
                await queueClient.CreateIfNotExistsAsync();

                // Generate safe blob name with sanitized filename
                var sanitizedFileName = SanitizeFileName(file.FileName);
                var blobName = $"{Guid.NewGuid()}{Path.GetExtension(sanitizedFileName)}";
                var blobClient = containerClient.GetBlobClient(blobName);

                // Upload to blob storage
                using var stream = file.OpenReadStream();
                await blobClient.UploadAsync(stream, overwrite: true);

                // Save metadata to database
                var image = new Image
                {
                    FileName = sanitizedFileName,
                    ContentType = validatedContentType,
                    Size = file.Length,
                    BlobUrl = blobClient.Uri.ToString(),
                    ThumbnailProcessed = false
                };

                db.Images.Add(image);
                await db.SaveChangesAsync();

                // Queue thumbnail generation
                var message = JsonSerializer.Serialize(new
                {
                    imageId = image.Id,
                    blobName = blobName
                });
                await queueClient.SendMessageAsync(message);

                logger.LogInformation("Image {ImageId} uploaded: {FileName}, queued for thumbnail generation",
                    image.Id, file.FileName);

                return Results.Created($"/api/images/{image.Id}", ImageDto.FromImage(image));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to upload image");
                return Results.Problem("Failed to upload image");
            }
        });

        // Delete image
        group.MapDelete("/images/{id}", async (
            int id,
            IAntiforgery antiforgery,
            HttpContext context,
            ImageDbContext db,
            BlobContainerClient containerClient,
            ILogger<Program> logger) =>
        {
            var antiforgeryValidationResult = await ValidateAntiforgeryAsync(antiforgery, context);
            if (antiforgeryValidationResult is not null)
            {
                return antiforgeryValidationResult;
            }

            var image = await db.Images.FindAsync(id);
            if (image is null)
            {
                return Results.NotFound();
            }

            try
            {
                // Delete from blob storage
                var blobName = image.BlobUrl.Split('/').Last();
                await containerClient.DeleteBlobIfExistsAsync(blobName);

                // Delete thumbnail if exists
                if (image.ThumbnailUrl is not null)
                {
                    var thumbnailName = image.ThumbnailUrl.Split('/').Last();
                    await containerClient.DeleteBlobIfExistsAsync(thumbnailName);
                }

                // Delete from database
                db.Images.Remove(image);
                await db.SaveChangesAsync();

                logger.LogInformation("Image {ImageId} deleted: {FileName}", id, image.FileName);

                return Results.Ok(new { message = $"Image {id} deleted" });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to delete image {ImageId}", id);
                return Results.Problem("Failed to delete image");
            }
        });

        return app;
    }

    private static string? GetValidatedImageContentType(
        IFormFile file,
        string fileExtension,
        CancellationToken cancellationToken)
    {
        if (!AllowedFormatsByExtension.TryGetValue(fileExtension, out var expectedFormat))
        {
            return null;
        }

        cancellationToken.ThrowIfCancellationRequested();

        using var detectionStream = file.OpenReadStream();
        using var codec = SKCodec.Create(detectionStream);

        if (codec is null ||
            codec.EncodedFormat != expectedFormat ||
            codec.Info.Width <= 0 ||
            codec.Info.Height <= 0 ||
            (long)codec.Info.Width * codec.Info.Height > MaxPixelCount ||
            !ContentTypesByFormat.TryGetValue(codec.EncodedFormat, out var contentType))
        {
            return null;
        }

        cancellationToken.ThrowIfCancellationRequested();

        using var decodeStream = file.OpenReadStream();
        using var bitmap = SKBitmap.Decode(decodeStream);

        return bitmap is not null && !bitmap.IsNull && bitmap.ReadyToDraw
            ? contentType
            : null;
    }

    private static async Task<IResult?> ValidateAntiforgeryAsync(IAntiforgery antiforgery, HttpContext context)
    {
        try
        {
            await antiforgery.ValidateRequestAsync(context);
            return null;
        }
        catch (AntiforgeryValidationException)
        {
            return Results.BadRequest(new { error = "Invalid or missing antiforgery token" });
        }
    }

    private static string SanitizeFileName(string fileName)
    {
        // Remove path separators and invalid characters
        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = string.Concat(fileName.Split(invalidChars));

        // Limit length
        if (sanitized.Length > 255)
        {
            var extension = Path.GetExtension(sanitized);
            var nameWithoutExt = Path.GetFileNameWithoutExtension(sanitized);
            sanitized = nameWithoutExt[..(255 - extension.Length)] + extension;
        }

        return sanitized;
    }
}
