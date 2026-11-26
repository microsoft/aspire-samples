namespace VolumeMount.Data;

/// <summary>An equipment inspection recorded by a field technician. Stored in SQL Server.</summary>
public class Inspection
{
    public int Id { get; set; }

    public required string AssetTag { get; set; }

    public required string AssetName { get; set; }

    public required string Site { get; set; }

    public required string Inspector { get; set; }

    public InspectionOutcome Outcome { get; set; }

    public string? Notes { get; set; }

    public DateTimeOffset InspectedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public List<InspectionAttachment> Attachments { get; set; } = [];
}

public enum InspectionOutcome
{
    Pass,
    Attention,
    Fail
}

/// <summary>Metadata for a photo or document attached to an inspection. The content lives in Azure Blob Storage.</summary>
public class InspectionAttachment
{
    public int Id { get; set; }

    public int InspectionId { get; set; }

    public required string FileName { get; set; }

    public required string ContentType { get; set; }

    public long SizeBytes { get; set; }

    public required string BlobName { get; set; }

    public DateTimeOffset UploadedAt { get; set; }
}
