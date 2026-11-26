using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using VolumeMount.Data;

namespace VolumeMount.BlazorWeb.Inspections;

/// <summary>The fields a technician fills in for a new inspection. Shared by the API and the Blazor form.</summary>
public sealed class InspectionForm
{
    [Display(Name = "Asset tag"), Required, StringLength(32, MinimumLength = 2), RegularExpression("^[A-Za-z0-9][A-Za-z0-9-]*$", ErrorMessage = "Use letters, numbers, and dashes, e.g. AHU-201.")]
    [Description("Asset tag printed on the equipment, e.g. `AHU-201`.")]
    public string AssetTag { get; set; } = "";

    [Display(Name = "Equipment"), Required, StringLength(120, MinimumLength = 2)]
    [Description("Human-friendly name of the equipment.")]
    public string AssetName { get; set; } = "";

    [Display(Name = "Site"), Required, StringLength(80, MinimumLength = 2)]
    [Description("Facility where the equipment is installed.")]
    public string Site { get; set; } = "";

    [Display(Name = "Inspector"), Required, StringLength(80, MinimumLength = 2)]
    [Description("Technician who performed the inspection.")]
    public string Inspector { get; set; } = "";

    [Description("Result of the inspection: `Pass`, `Attention` (follow-up needed), or `Fail` (take out of service).")]
    public InspectionOutcome Outcome { get; set; } = InspectionOutcome.Pass;

    [Display(Name = "Findings"), StringLength(2000)]
    [Description("Findings, readings, and follow-up actions.")]
    public string? Notes { get; set; }
}

/// <summary>A file to store in Blob Storage alongside an inspection.</summary>
public sealed record AttachmentUpload(string FileName, string ContentType, long Length, Func<Stream> OpenReadStream);

[Description("An inspection in a list, with attachment totals.")]
public sealed record InspectionSummary(
    [property: Description("Database ID of the inspection.")] int Id,
    [property: Description("Display reference, e.g. `INS-0042`.")] string Reference,
    string AssetTag,
    string AssetName,
    string Site,
    string Inspector,
    InspectionOutcome Outcome,
    DateTimeOffset InspectedAt,
    [property: Description("Number of photos and documents stored in Blob Storage.")] int AttachmentCount,
    [property: Description("Total size of the attachments in bytes.")] long AttachmentBytes,
    [property: Description("ID of the first image attachment, used as a thumbnail.")] int? CoverAttachmentId);

[Description("An inspection with its notes and attachment metadata.")]
public sealed record InspectionDetail(
    int Id,
    string Reference,
    string AssetTag,
    string AssetName,
    string Site,
    string Inspector,
    InspectionOutcome Outcome,
    string? Notes,
    DateTimeOffset InspectedAt,
    DateTimeOffset CreatedAt,
    IReadOnlyList<AttachmentInfo> Attachments);

[Description("Metadata for a file stored in Blob Storage. Download the content from `contentUrl`.")]
public sealed record AttachmentInfo(
    int Id,
    string FileName,
    string ContentType,
    long SizeBytes,
    [property: Description("Blob name inside the `inspection-attachments` container.")] string BlobName,
    DateTimeOffset UploadedAt,
    [property: Description("Relative URL that streams the file from Blob Storage.")] string ContentUrl);

[Description("What's persisted in SQL Server and Blob Storage right now, and when SQL Server last started.")]
public sealed record StorageStatus(
    [property: Description("Inspection rows in SQL Server.")] int InspectionCount,
    [property: Description("Attachment rows in SQL Server.")] int AttachmentCount,
    [property: Description("Blobs in the attachments container.")] int BlobCount,
    [property: Description("Total size of the blobs in bytes.")] long BlobBytes,
    [property: Description("Blob container that stores the attachments.")] string BlobContainer,
    [property: Description("When the SQL Server process last started (UTC). Restarting the container resets this; the data stays.")] DateTimeOffset SqlServerStartedAt,
    [property: Description("Path of the database's data file inside the container, which sits on the mounted data volume.")] string DatabaseFile);

[Description("Result of seeding sample data.")]
public sealed record SeedResult(
    [property: Description("Inspections created by this request.")] int InspectionsCreated,
    [property: Description("Attachments uploaded to Blob Storage by this request.")] int AttachmentsUploaded,
    [property: Description("Total inspections now stored.")] int TotalInspections);

public static class InspectionReference
{
    public static string Format(int id) => $"INS-{id:0000}";
}
