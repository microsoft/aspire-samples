using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Api.Data;
using Api.Models;
using Api.Services;
using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Api.Extensions;

public static class ImageEndpoints
{
    private const string ThumbnailContentType = "image/jpeg";

    // Multipart framing adds a little overhead on top of the 10 MB photo limit.
    private const long MaxRequestBytes = PhotoIngestService.MaxFileSizeBytes + 64 * 1024;

    public static WebApplication MapImages(this WebApplication app)
    {
        var api = app.MapGroup("/api");

        api.MapGet("/antiforgery", (IAntiforgery antiforgery, HttpContext context) =>
                TypedResults.Ok(new AntiforgeryTokenResponse(antiforgery.GetAndStoreTokens(context).RequestToken)))
            .WithName("GetAntiforgeryToken")
            .WithTags("Security")
            .WithSummary("Get an antiforgery token")
            .WithDescription("Issues an antiforgery cookie and returns the matching request token. Browsers send the token in the `RequestVerificationToken` header when uploading or deleting photos.");

        var photos = api.MapGroup("/images").WithTags("Photos");

        photos.MapGet("", async (
                ImageDbContext db,
                [Description("Number of photos to skip.")][Range(0, int.MaxValue)] int skip = 0,
                [Description("Number of photos to return (1-100).")][Range(1, 100)] int take = 100) =>
            {
                var images = await db.Images
                    .AsNoTracking()
                    .OrderByDescending(i => i.UploadedAt)
                    .Skip(Math.Max(0, skip))
                    .Take(Math.Clamp(take, 1, 100))
                    .ToListAsync();

                return TypedResults.Ok(images.Select(ImageDto.FromImage).ToList());
            })
            .WithName("ListPhotos")
            .WithSummary("List catalog photos")
            .WithDescription("Returns product photos, newest first, with each photo's thumbnail status. The gallery polls this while thumbnails are processing.");

        photos.MapGet("/{id:int}", async Task<Results<Ok<ImageDto>, ProblemHttpResult>> (
                [Description("The photo ID.")] int id,
                ImageDbContext db) =>
            {
                var image = await db.Images.FindAsync(id);
                return image is null ? PhotoNotFound(id) : TypedResults.Ok(ImageDto.FromImage(image));
            })
            .WithName("GetPhoto")
            .WithSummary("Get a photo")
            .WithDescription("Returns the metadata and thumbnail status for a single product photo.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        photos.MapGet("/{id:int}/blob", async Task<Results<FileStreamHttpResult, ProblemHttpResult>> (
                [Description("The photo ID.")] int id,
                ImageDbContext db,
                BlobContainerClient containerClient) =>
            {
                var image = await db.Images.FindAsync(id);
                if (image is null)
                {
                    return PhotoNotFound(id);
                }

                var download = await containerClient.GetBlobClient(image.BlobUrl.Split('/').Last()).DownloadStreamingAsync();
                return TypedResults.Stream(download.Value.Content, image.ContentType);
            })
            .WithName("GetPhotoOriginal")
            .WithSummary("Download the original photo")
            .WithDescription("Streams the original upload from Blob Storage with its detected content type.")
            .Produces(StatusCodes.Status200OK, contentType: "image/jpeg", additionalContentTypes: ["image/png", "image/gif", "image/webp"])
            .ProducesProblem(StatusCodes.Status404NotFound);

        photos.MapGet("/{id:int}/thumbnail", async Task<Results<FileStreamHttpResult, ProblemHttpResult>> (
                [Description("The photo ID.")] int id,
                ImageDbContext db,
                BlobContainerClient containerClient) =>
            {
                var image = await db.Images.FindAsync(id);
                if (image is null)
                {
                    return PhotoNotFound(id);
                }

                if (image.ThumbnailUrl is null)
                {
                    return TypedResults.Problem(
                        title: "Thumbnail not ready",
                        detail: $"The thumbnail for photo {id} is still being generated.",
                        statusCode: StatusCodes.Status404NotFound);
                }

                var download = await containerClient.GetBlobClient(image.ThumbnailUrl.Split('/').Last()).DownloadStreamingAsync();
                return TypedResults.Stream(
                    download.Value.Content,
                    string.IsNullOrWhiteSpace(download.Value.Details.ContentType) ? ThumbnailContentType : download.Value.Details.ContentType);
            })
            .WithName("GetPhotoThumbnail")
            .WithSummary("Download the catalog thumbnail")
            .WithDescription("Streams the 300×300 JPEG thumbnail generated by the worker. Returns 404 problem details until the thumbnail is ready.")
            .Produces(StatusCodes.Status200OK, contentType: ThumbnailContentType)
            .ProducesProblem(StatusCodes.Status404NotFound);

        photos.MapPost("", async Task<Results<Created<ImageDto>, ProblemHttpResult>> (
                [Description("The product photo (JPEG, PNG, GIF or WebP, up to 10 MB).")] IFormFile file,
                IAntiforgery antiforgery,
                HttpContext context,
                PhotoIngestService ingest,
                ILogger<PhotoIngestService> logger) =>
            {
                if (await ValidateAntiforgeryAsync(antiforgery, context) is { } invalidToken)
                {
                    return invalidToken;
                }

                if (ingest.Validate(file.FileName, file.ContentType, file.Length, file.OpenReadStream, out var photo) is { } problem)
                {
                    return problem;
                }

                try
                {
                    var image = await ingest.StoreAsync(file.FileName, file.Length, photo, file.OpenReadStream, "upload", context.RequestAborted);
                    return TypedResults.Created($"/api/images/{image.Id}", ImageDto.FromImage(image));
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.UploadFailed(ex, file.FileName);
                    return TypedResults.Problem(
                        title: "Upload failed",
                        detail: "The photo couldn't be stored. Try again in a moment.",
                        statusCode: StatusCodes.Status500InternalServerError);
                }
            })
            .WithName("UploadPhoto")
            .WithSummary("Upload a product photo")
            .WithDescription("""
                Accepts a `multipart/form-data` upload with a single `file` field (max 10 MB; JPEG, PNG, GIF or WebP).
                The image bytes are validated, the original is stored in Blob Storage, metadata is written to SQL,
                and a thumbnail request is published to the `thumbnails` queue with the caller's W3C trace context.
                Requires the `RequestVerificationToken` header from `GET /api/antiforgery`.
                """)
            .WithMetadata(new RequestSizeLimitAttribute(MaxRequestBytes))
            .WithFormOptions(multipartBodyLengthLimit: MaxRequestBytes)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        photos.MapDelete("/{id:int}", async Task<Results<NoContent, ProblemHttpResult>> (
                [Description("The photo ID.")] int id,
                IAntiforgery antiforgery,
                HttpContext context,
                ImageDbContext db,
                BlobContainerClient containerClient,
                ILogger<PhotoIngestService> logger) =>
            {
                if (await ValidateAntiforgeryAsync(antiforgery, context) is { } invalidToken)
                {
                    return invalidToken;
                }

                var image = await db.Images.FindAsync(id);
                if (image is null)
                {
                    return PhotoNotFound(id);
                }

                try
                {
                    await containerClient.DeleteBlobIfExistsAsync(image.BlobUrl.Split('/').Last());
                    if (image.ThumbnailUrl is not null)
                    {
                        await containerClient.DeleteBlobIfExistsAsync(image.ThumbnailUrl.Split('/').Last());
                    }

                    db.Images.Remove(image);
                    await db.SaveChangesAsync();

                    logger.PhotoDeleted(id, image.FileName);
                    return TypedResults.NoContent();
                }
                catch (Exception ex)
                {
                    logger.DeleteFailed(ex, id);
                    return TypedResults.Problem(title: "Delete failed", detail: $"Photo {id} couldn't be deleted.", statusCode: StatusCodes.Status500InternalServerError);
                }
            })
            .WithName("DeletePhoto")
            .WithSummary("Delete a photo")
            .WithDescription("Deletes the original, its thumbnail and the catalog record. Requires the `RequestVerificationToken` header.")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        return app;
    }

    // Development-only operations behind the AppHost's dashboard commands.
    public static WebApplication MapOperations(this WebApplication app)
    {
        var operations = app.MapGroup("/api/operations").WithTags("Operations");

        operations.MapPost("/sample-photos", async (
                PhotoIngestService ingest,
                IHostEnvironment environment,
                ILogger<PhotoIngestService> logger,
                CancellationToken cancellationToken) =>
            {
                var files = Directory.GetFiles(Path.Combine(environment.ContentRootPath, "SamplePhotos")).Order().ToList();
                var uploaded = new List<ImageDto>();

                foreach (var path in files)
                {
                    var fileName = Path.GetFileName(path);
                    var size = new FileInfo(path).Length;
                    Stream Open() => File.OpenRead(path);

                    if (ingest.Validate(fileName, null, size, Open, out var photo) is null)
                    {
                        uploaded.Add(ImageDto.FromImage(await ingest.StoreAsync(fileName, size, photo, Open, "sample", cancellationToken)));
                    }
                }

                logger.SamplePhotosUploaded(uploaded.Count);
                return TypedResults.Ok(new SamplePhotosResult(uploaded.Count, uploaded));
            })
            .WithName("UploadSamplePhotos")
            .WithSummary("Upload sample product photos")
            .WithDescription("Development only. Runs the bundled studio product photos through the same upload pipeline as `POST /api/images`.");

        operations.MapPost("/reprocess-thumbnails", async (
                ImageDbContext db,
                PhotoIngestService ingest,
                ILogger<PhotoIngestService> logger,
                CancellationToken cancellationToken) =>
            {
                await db.Images.ExecuteUpdateAsync(s => s.SetProperty(i => i.ThumbnailProcessed, false), cancellationToken);
                var images = await db.Images.AsNoTracking().OrderBy(i => i.Id).ToListAsync(cancellationToken);

                foreach (var image in images)
                {
                    await ingest.RequestThumbnailAsync(image, "reprocess", cancellationToken);
                }

                logger.ThumbnailsReprocessed(images.Count);
                return TypedResults.Ok(new ReprocessResult(images.Count));
            })
            .WithName("ReprocessThumbnails")
            .WithSummary("Reprocess all thumbnails")
            .WithDescription("Development only. Marks every photo as processing and re-publishes a thumbnail request for each, for example after changing the thumbnail size.");

        return app;
    }

    private static ProblemHttpResult PhotoNotFound(int id) => TypedResults.Problem(
        title: "Photo not found",
        detail: $"No photo exists with ID {id}.",
        statusCode: StatusCodes.Status404NotFound);

    private static async Task<ProblemHttpResult?> ValidateAntiforgeryAsync(IAntiforgery antiforgery, HttpContext context)
    {
        try
        {
            await antiforgery.ValidateRequestAsync(context);
            return null;
        }
        catch (AntiforgeryValidationException)
        {
            return TypedResults.Problem(
                title: "Invalid antiforgery token",
                detail: "Send the token from GET /api/antiforgery in the RequestVerificationToken header.",
                statusCode: StatusCodes.Status400BadRequest);
        }
    }
}
