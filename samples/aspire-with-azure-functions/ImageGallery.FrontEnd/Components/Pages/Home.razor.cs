using System.Globalization;
using ImageGallery.FrontEnd.Claims;
using Microsoft.AspNetCore.Components.Forms;

namespace ImageGallery.FrontEnd.Components.Pages;

public sealed partial class Home(ClaimPhotoCatalog catalog, ClaimPhotoIntake intake) : IDisposable
{
    private const int MaxFilesPerUpload = 20;

    private readonly List<string> _problems = [];
    private IReadOnlyList<ClaimPhoto> _photos = [];
    private string _selectedClaim = ClaimDirectory.OpenClaims[0].Number;
    private bool _isUploading;
    private int _uploadTotal;
    private int _uploadedCount;
    private Guid _inputKey = Guid.CreateVersion7();

    protected override void OnInitialized()
    {
        _photos = catalog.GetPhotos();
        catalog.Changed += OnCatalogChanged;
    }

    public void Dispose() => catalog.Changed -= OnCatalogChanged;

    // Raised by uploads and by the function's results arriving on the queue, from any circuit or the worker.
    private void OnCatalogChanged() => _ = InvokeAsync(() =>
    {
        _photos = catalog.GetPhotos();
        StateHasChanged();
    });

    private async Task OnFilesSelectedAsync(InputFileChangeEventArgs e)
    {
        _problems.Clear();

        if (e.FileCount > MaxFilesPerUpload)
        {
            _problems.Add($"Choose up to {MaxFilesPerUpload} photos at a time.");
            return;
        }

        var files = e.GetMultipleFiles(MaxFilesPerUpload);
        (_isUploading, _uploadTotal, _uploadedCount) = (true, files.Count, 0);

        try
        {
            foreach (var file in files)
            {
                try
                {
                    var result = await intake.UploadAsync(
                        _selectedClaim, file.Name, file.Size, () => file.OpenReadStream(ClaimPhotoIntake.MaxPhotoBytes), "browser", CancellationToken.None);

                    if (result.Problem is { } problem)
                    {
                        _problems.Add($"{file.Name}: {problem.Detail}");
                    }
                }
                catch (Exception)
                {
                    // ClaimPhotoIntake has already logged the failure and marked its span as failed.
                    _problems.Add($"{file.Name}: the upload didn't finish. Try again.");
                }

                _uploadedCount++;
                StateHasChanged();
            }
        }
        finally
        {
            _isUploading = false;
            _inputKey = Guid.CreateVersion7();
        }
    }

    private int Count(PhotoStatus status) => _photos.Count(p => p.Status == status);

    private static string StatusClass(PhotoStatus status) => status switch
    {
        PhotoStatus.Ready => "ready",
        PhotoStatus.RetakeRequested => "retake",
        PhotoStatus.Unreadable => "unreadable",
        _ => "processing"
    };

    private static string StatusLabel(PhotoStatus status) => status switch
    {
        PhotoStatus.Ready => "Ready for review",
        PhotoStatus.RetakeRequested => "Retake requested",
        PhotoStatus.Unreadable => "Unreadable",
        _ => "Processing"
    };

    private static string Describe(ClaimPhoto photo) => photo is { Width: { } width, Height: { } height }
        ? $"{width} × {height} · {FormatSize(photo.SizeBytes)}"
        : FormatSize(photo.SizeBytes);

    private static string Note(ClaimPhoto photo) => photo switch
    {
        { Status: PhotoStatus.RetakeRequested } => "Below the 720p minimum",
        { Status: PhotoStatus.Unreadable } => "Not a valid image",
        { Status: PhotoStatus.Processing } => "Waiting for the function",
        { Turnaround: { } turnaround } when turnaround > TimeSpan.Zero => $"Checked in {turnaround.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture)} s",
        _ => "Checked"
    };

    private static string FormatSize(long bytes) => bytes switch
    {
        >= 1024 * 1024 => $"{(bytes / (1024d * 1024)).ToString("0.0", CultureInfo.InvariantCulture)} MB",
        >= 1024 => $"{bytes / 1024} KB",
        _ => $"{bytes} B"
    };
}
