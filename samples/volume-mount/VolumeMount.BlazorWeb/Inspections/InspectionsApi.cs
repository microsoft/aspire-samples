using System.ComponentModel;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using VolumeMount.Data;

namespace VolumeMount.BlazorWeb.Inspections;

public static class InspectionsApi
{
    public static void MapInspectionsApi(this IEndpointRouteBuilder routes, IHostEnvironment environment)
    {
        var api = routes.MapGroup("/api")
            // Called by API clients and dashboard commands rather than browser forms, so no antiforgery token
            .DisableAntiforgery();

        var inspections = api.MapGroup("/inspections").WithTags("Inspections");

        inspections.MapGet("/", async (
                [Description("Only return inspections with this outcome.")] InspectionOutcome? outcome,
                [Description("Only return inspections at this site, e.g. `Seattle Data Center 1`.")] string? site,
                InspectionService service,
                CancellationToken cancellationToken,
                [Description("Maximum number of inspections to return (1-200).")] int take = 50) =>
                TypedResults.Ok(await service.ListAsync(outcome, site, Math.Clamp(take, 1, 200), cancellationToken)))
            .WithName("ListInspections")
            .WithSummary("List inspections")
            .WithDescription("Returns the most recent inspections from SQL Server, newest first, with attachment counts and a thumbnail attachment ID.");

        inspections.MapGet("/{id:int}", async Task<Results<Ok<InspectionDetail>, ProblemHttpResult>> (
                [Description("Database ID of the inspection.")] int id,
                InspectionService service,
                CancellationToken cancellationToken) =>
                await service.GetAsync(id, cancellationToken) is { } detail
                    ? TypedResults.Ok(detail)
                    : NotFound($"Inspection {InspectionReference.Format(id)} doesn't exist."))
            .WithName("GetInspection")
            .WithSummary("Get an inspection")
            .WithDescription("Returns an inspection with its notes and the metadata of each attachment stored in Blob Storage.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        inspections.MapPost("/", async Task<Results<Created<InspectionDetail>, ValidationProblem, ProblemHttpResult>> (
                [FromForm] InspectionForm form,
                [Description("Photos or documents (JPEG, PNG, WebP, SVG, PDF, TXT, CSV; up to 6 files of 10 MB each).")] IFormFileCollection? attachments,
                InspectionService service,
                ILogger<InspectionService> logger,
                CancellationToken cancellationToken) =>
            {
                var files = AttachmentRules.FromFormFiles(attachments ?? new FormFileCollection());
                if (AttachmentRules.Validate(files) is { } error)
                {
                    logger.InspectionRejected(form.AssetTag, error);
                    return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["attachments"] = [error] });
                }

                try
                {
                    var detail = await service.CreateAsync(form, files, source: "api", cancellationToken: cancellationToken);
                    return TypedResults.Created($"/api/inspections/{detail.Id}", detail);
                }
                catch (AttachmentStorageException ex)
                {
                    return StorageUnavailable(ex);
                }
            })
            .WithName("CreateInspection")
            .WithSummary("Record an inspection")
            .WithDescription("""
                Accepts a `multipart/form-data` submission with the inspection fields and optional attachments.
                Attachments are uploaded to the `inspection-attachments` blob container first, then the inspection and attachment
                metadata are inserted into SQL Server in one transaction. If an upload fails, nothing is saved and a 503 is returned.
                """)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        api.MapGet("/attachments/{id:int}/content", async Task<Results<FileStreamHttpResult, ProblemHttpResult>> (
                [Description("Database ID of the attachment.")] int id,
                InspectionService service,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
            {
                if (await service.OpenAttachmentAsync(id, cancellationToken) is not { } file)
                {
                    return NotFound($"Attachment {id} doesn't exist.");
                }

                // Uploaded files are untrusted: never let the browser sniff or run scripts in them (e.g. SVG)
                httpContext.Response.Headers.ContentSecurityPolicy = "default-src 'none'; style-src 'unsafe-inline'; sandbox";
                httpContext.Response.Headers.XContentTypeOptions = "nosniff";
                httpContext.Response.Headers.CacheControl = "private, max-age=86400, immutable";
                httpContext.Response.Headers.ContentDisposition = new ContentDispositionHeaderValue("inline") { FileNameStar = file.Attachment.FileName }.ToString();
                return TypedResults.Stream(file.Content, file.Attachment.ContentType);
            })
            .WithTags("Attachments")
            .WithName("GetAttachmentContent")
            .WithSummary("Download an attachment")
            .WithDescription("Streams an attachment's content from Blob Storage with its original content type.")
            .Produces(StatusCodes.Status200OK, contentType: "application/octet-stream")
            .ProducesProblem(StatusCodes.Status404NotFound);

        api.MapGet("/storage/status", async (InspectionService service, ILogger<InspectionService> logger, CancellationToken cancellationToken) =>
            {
                var status = await service.GetStorageStatusAsync(cancellationToken);
                logger.StorageStatusChecked(status.InspectionCount, status.SqlServerStartedAt, status.BlobCount, status.BlobContainer);
                return TypedResults.Ok(status);
            })
            .WithTags("Storage")
            .WithName("GetStorageStatus")
            .WithSummary("Check persisted data")
            .WithDescription("""
                Counts the inspections in SQL Server and the blobs in the attachments container, and reports when SQL Server last started.
                Restart the `sqlserver` resource from the Aspire dashboard and call this again: `sqlServerStartedAt` changes, the counts don't,
                because the database files live on a Docker volume.
                """);

        if (environment.IsDevelopment())
        {
            api.MapPost("/dev/seed", async (SampleData sampleData, CancellationToken cancellationToken) =>
                    TypedResults.Ok(await sampleData.SeedAsync(cancellationToken)))
                .WithTags("Development")
                .WithName("SeedSampleData")
                .WithSummary("Seed sample data")
                .WithDescription("Development only. Records one inspection for each of 10 facility assets across the past week, each with a nameplate capture and a CSV reading log.");
        }
    }

    private static ProblemHttpResult NotFound(string detail) =>
        TypedResults.Problem(title: "Not found", detail: detail, statusCode: StatusCodes.Status404NotFound);

    private static ProblemHttpResult StorageUnavailable(AttachmentStorageException ex) =>
        TypedResults.Problem(title: "Attachment storage unavailable", detail: $"{ex.Message} The inspection wasn't saved; try again.", statusCode: StatusCodes.Status503ServiceUnavailable);
}
