using System.ComponentModel;
using Azure;
using Azure.Storage.Blobs;
using ImageGallery.Shared;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Net.Http.Headers;

namespace ImageGallery.FrontEnd.Claims;

public static class ClaimPhotoApi
{
    private const string ClaimNumberDescription = "Claim number, for example `CLM-26-10482`.";
    private const string FileNameDescription = "The photo's `fileName`, for example `rear-bumper-damage.jpg`.";

    public static string PhotoUrl(ClaimPhoto photo) => $"/api/claims/{photo.ClaimNumber}/photos/{photo.FileName}";

    public static string? ThumbnailUrl(ClaimPhoto photo) =>
        photo is { Status: PhotoStatus.Ready or PhotoStatus.RetakeRequested, ProcessedAt: { } processedAt }
            ? $"{PhotoUrl(photo)}/thumbnail?v={processedAt.ToUnixTimeMilliseconds()}"
            : null;

    public static IEndpointRouteBuilder MapClaimPhotoApi(this IEndpointRouteBuilder app)
    {
        var claims = app.MapGroup("/api/claims");

        claims.MapGet("/", (ClaimPhotoCatalog catalog) =>
                TypedResults.Ok(ClaimDirectory.OpenClaims.Select(c => ClaimSummary.From(c, catalog.GetPhotos(c.Number))).ToList()))
            .WithName("ListClaims")
            .WithTags("Claims")
            .WithSummary("List open claims")
            .WithDescription("Returns the open claims that accept photos, with how many photos each has and how many are ready for an adjuster.");

        claims.MapGet("/{claimNumber}/photos", Results<Ok<List<ClaimPhotoDto>>, ProblemHttpResult> (
                [Description(ClaimNumberDescription)] string claimNumber,
                ClaimPhotoCatalog catalog) =>
            ClaimDirectory.Find(claimNumber) is { } claim
                ? TypedResults.Ok(catalog.GetPhotos(claim.Number).Select(ClaimPhotoDto.From).ToList())
                : ClaimNotFound(claimNumber))
            .WithName("ListClaimPhotos")
            .WithTags("Photos")
            .WithSummary("List a claim's photos")
            .WithDescription("Returns every photo attached to the claim, oldest first, with its review status. Photos stay `processing` until the blob-triggered Azure Function's result arrives.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        claims.MapPost("/{claimNumber}/photos", async Task<Results<Accepted<ClaimPhotoDto>, ProblemHttpResult>> (
                [Description(ClaimNumberDescription)] string claimNumber,
                [Description("A JPEG, PNG or WebP damage photo, up to 10 MB.")] IFormFile photo,
                ClaimPhotoIntake intake,
                CancellationToken cancellationToken) =>
            {
                var result = await intake.UploadAsync(claimNumber, photo.FileName, photo.Length, photo.OpenReadStream, "api", cancellationToken);

                return result.Photo is { } accepted
                    ? TypedResults.Accepted(PhotoUrl(accepted), ClaimPhotoDto.From(accepted))
                    : TypedResults.Problem(title: result.Problem!.Title, detail: result.Problem.Detail, statusCode: result.Problem.StatusCode);
            })
            .WithName("UploadClaimPhoto")
            .WithTags("Photos")
            .WithSummary("Upload a damage photo")
            .WithDescription("""
                Stores the photo in the `claim-photos` blob container and returns `202 Accepted` with the photo in the `processing` state.
                The new blob fires the Azure Function's blob trigger, which checks the resolution (adjusters need at least 720p),
                renders a 480 px thumbnail and queues the result; poll `GET /api/claims/{claimNumber}/photos` or watch the intake page
                for the final status. The upload's W3C trace context is stored in the blob's metadata, so the whole round trip is one trace.
                """)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
            // API clients (an adjuster's mobile app, curl, Scalar) don't send cookies, so there's no ambient credential
            // for a cross-site request to abuse. The sample has no authentication; add it before exposing this API.
            .DisableAntiforgery();

        claims.MapGet("/{claimNumber}/photos/{fileName}", (
                [Description(ClaimNumberDescription)] string claimNumber,
                [Description(FileNameDescription)] string fileName,
                BlobServiceClient blobs,
                CancellationToken cancellationToken) =>
                StreamBlobAsync(blobs, ClaimPhotoStorage.PhotosContainer, claimNumber, fileName, "Photo not found", cancellationToken))
            .WithName("DownloadClaimPhoto")
            .WithTags("Photos")
            .WithSummary("Download the original photo")
            .WithDescription("Streams the photo as uploaded, with its content type, `ETag` and `Last-Modified` headers.")
            .Produces<Stream>(StatusCodes.Status200OK, "image/jpeg", "image/png", "image/webp")
            .ProducesProblem(StatusCodes.Status404NotFound);

        claims.MapGet("/{claimNumber}/photos/{fileName}/thumbnail", (
                [Description(ClaimNumberDescription)] string claimNumber,
                [Description(FileNameDescription)] string fileName,
                BlobServiceClient blobs,
                CancellationToken cancellationToken) =>
                StreamBlobAsync(blobs, ClaimPhotoStorage.ThumbnailsContainer, claimNumber, fileName, "Thumbnail not ready", cancellationToken))
            .WithName("DownloadClaimPhotoThumbnail")
            .WithTags("Photos")
            .WithSummary("Download the thumbnail")
            .WithDescription("Streams the 480 px JPEG thumbnail rendered by the Azure Function. Returns `404` until the function has processed the photo.")
            .Produces<Stream>(StatusCodes.Status200OK, "image/jpeg")
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    // Development-only operations behind the AppHost's dashboard commands.
    public static IEndpointRouteBuilder MapIntakeOperations(this IEndpointRouteBuilder app)
    {
        var operations = app.MapGroup("/api/operations").WithTags("Operations");

        operations.MapPost("/sample-photos", async (
                ClaimPhotoIntake intake,
                IHostEnvironment environment,
                ILogger<ClaimPhotoIntake> logger,
                CancellationToken cancellationToken) =>
            {
                var uploaded = new List<ClaimPhotoDto>();
                foreach (var claimFolder in Directory.GetDirectories(Path.Combine(environment.ContentRootPath, "SamplePhotos")).Order())
                {
                    foreach (var path in Directory.GetFiles(claimFolder).Order())
                    {
                        var result = await intake.UploadAsync(
                            Path.GetFileName(claimFolder), Path.GetFileName(path), new FileInfo(path).Length, () => File.OpenRead(path), "sample", cancellationToken);

                        if (result.Photo is { } photo)
                        {
                            uploaded.Add(ClaimPhotoDto.From(photo));
                        }
                    }
                }

                logger.SamplePhotosUploaded(uploaded.Count);
                return TypedResults.Ok(new SamplePhotosResult(uploaded.Count, uploaded));
            })
            .WithName("UploadSamplePhotos")
            .WithSummary("Upload sample claim photos")
            .WithDescription("Development only. Runs the bundled damage photos for the three open claims through the same pipeline as `POST /api/claims/{claimNumber}/photos`.");

        operations.MapDelete("/photos", async (
                BlobServiceClient blobs,
                ClaimPhotoCatalog catalog,
                ILogger<ClaimPhotoIntake> logger,
                CancellationToken cancellationToken) =>
            {
                foreach (var containerName in (string[])[ClaimPhotoStorage.PhotosContainer, ClaimPhotoStorage.ThumbnailsContainer])
                {
                    var container = blobs.GetBlobContainerClient(containerName);
                    await foreach (var blob in container.GetBlobsAsync(cancellationToken: cancellationToken))
                    {
                        await container.DeleteBlobIfExistsAsync(blob.Name, cancellationToken: cancellationToken);
                    }
                }

                var deleted = catalog.Clear();
                logger.PhotosCleared(deleted);
                return TypedResults.Ok(new ClearPhotosResult(deleted));
            })
            .WithName("DeleteAllPhotos")
            .WithSummary("Delete all claim photos")
            .WithDescription("Development only. Deletes every photo and thumbnail so the intake page starts empty.");

        return app;
    }

    private static async Task<Results<FileStreamHttpResult, ProblemHttpResult>> StreamBlobAsync(
        BlobServiceClient blobs, string containerName, string claimNumber, string fileName, string notFoundTitle, CancellationToken cancellationToken)
    {
        if (ClaimDirectory.Find(claimNumber) is not { } claim || !ClaimPhotoFileNames.IsValid(fileName))
        {
            return TypedResults.Problem(title: notFoundTitle, detail: $"Claim '{claimNumber}' has no photo named '{fileName}'.", statusCode: StatusCodes.Status404NotFound);
        }

        try
        {
            var download = (await blobs.GetBlobContainerClient(containerName).GetBlobClient($"{claim.Number}/{fileName}").DownloadStreamingAsync(cancellationToken: cancellationToken)).Value;

            return TypedResults.Stream(download.Content, download.Details.ContentType,
                lastModified: download.Details.LastModified,
                entityTag: new EntityTagHeaderValue(download.Details.ETag.ToString("H")));
        }
        catch (RequestFailedException ex) when (ex.Status == StatusCodes.Status404NotFound)
        {
            return TypedResults.Problem(title: notFoundTitle, detail: $"Claim '{claim.Number}' has no {(containerName == ClaimPhotoStorage.ThumbnailsContainer ? "thumbnail" : "photo")} named '{fileName}' yet.", statusCode: StatusCodes.Status404NotFound);
        }
    }

    private static ProblemHttpResult ClaimNotFound(string claimNumber) =>
        TypedResults.Problem(title: "Claim not found", detail: $"There's no open claim '{claimNumber}'.", statusCode: StatusCodes.Status404NotFound);
}
