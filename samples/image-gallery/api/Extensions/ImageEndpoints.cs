using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Antiforgery;
using Azure.Storage.Blobs;
using Azure.Storage.Queues;
using Api.Data;
using Api.Models;
using System.Text.Json;

namespace Api.Extensions;

public static class ImageEndpoints
{
    private const long MaxFileSizeBytes = 10 * 1024 * 1024; // 10 MB
    private static readonly string[] AllowedImageFormats = [".jpg", ".jpeg", ".png", ".gif", ".webp"];

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
            return Results.Stream(download.Value.Content, image.ContentType);
        });

        // Upload image
        group.MapPost("/images", async (
            IFormFile file,
            ImageDbContext db,
            BlobContainerClient containerClient,
            QueueServiceClient queueService,
            ILogger<Program> logger) =>
        {
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
            if (!file.ContentType.StartsWith("image/"))
            {
                return Results.BadRequest(new { error = "File must be an image" });
            }

            // Validate file extension
            var fileExtension = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (string.IsNullOrEmpty(fileExtension) || !AllowedImageFormats.Contains(fileExtension))
            {
                return Results.BadRequest(new { error = $"File type not allowed. Allowed types: {string.Join(", ", AllowedImageFormats)}" });
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
                    ContentType = file.ContentType,
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
            ImageDbContext db,
            BlobContainerClient containerClient,
            ILogger<Program> logger) =>
        {
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
