using ImageGallery.Shared;

namespace ImageGallery.FrontEnd.Claims;

// In-memory view of every claim photo and its review status. Uploads add photos as "processing"; the function's
// results (read from the queue by PhotoResultsWorker) move them to "ready", "retake requested" or "unreadable".
// Open pages subscribe to Changed so they refresh as soon as a result arrives.
public sealed class ClaimPhotoCatalog(ClaimIntakeTelemetry telemetry)
{
    private readonly Lock _lock = new();
    private readonly Dictionary<string, ClaimPhoto> _photos = new(StringComparer.OrdinalIgnoreCase);

    public event Action? Changed;

    public IReadOnlyList<ClaimPhoto> GetPhotos(string? claimNumber = null)
    {
        lock (_lock)
        {
            return [.. _photos.Values
                .Where(p => claimNumber is null || string.Equals(p.ClaimNumber, claimNumber, StringComparison.OrdinalIgnoreCase))
                .OrderBy(p => p.UploadedAt)
                .ThenBy(p => p.FileName, StringComparer.Ordinal)];
        }
    }

    public ClaimPhoto? Find(string claimNumber, string fileName)
    {
        lock (_lock)
        {
            return _photos.GetValueOrDefault($"{claimNumber}/{fileName}");
        }
    }

    public void Load(IEnumerable<ClaimPhoto> photos)
    {
        lock (_lock)
        {
            var delta = -CountProcessing();
            _photos.Clear();
            foreach (var photo in photos)
            {
                _photos[photo.BlobName] = photo;
            }

            telemetry.BacklogChanged(delta + CountProcessing());
        }

        Changed?.Invoke();
    }

    public void Upsert(ClaimPhoto photo)
    {
        lock (_lock)
        {
            _photos.TryGetValue(photo.BlobName, out var previous);
            _photos[photo.BlobName] = photo;
            telemetry.BacklogChanged(BacklogDelta(previous, photo));
        }

        Changed?.Invoke();
    }

    public void Remove(ClaimPhoto photo)
    {
        lock (_lock)
        {
            if (_photos.Remove(photo.BlobName, out var previous))
            {
                telemetry.BacklogChanged(BacklogDelta(previous, null));
            }
        }

        Changed?.Invoke();
    }

    // Returns the updated photo, or null when the result is for a photo this catalog doesn't know about.
    public ClaimPhoto? ApplyResult(PhotoProcessed result)
    {
        ClaimPhoto? updated = null;
        lock (_lock)
        {
            var key = $"{result.ClaimNumber}/{result.FileName}";
            if (_photos.TryGetValue(key, out var previous))
            {
                updated = previous with
                {
                    Status = ClaimPhoto.StatusFor(result.Quality),
                    Width = result.Width,
                    Height = result.Height,
                    ProcessedAt = result.ProcessedAt
                };
                _photos[key] = updated;
                telemetry.BacklogChanged(BacklogDelta(previous, updated));
            }
        }

        Changed?.Invoke();
        return updated;
    }

    public int Clear()
    {
        int count;
        lock (_lock)
        {
            count = _photos.Count;
            telemetry.BacklogChanged(-CountProcessing());
            _photos.Clear();
        }

        Changed?.Invoke();
        return count;
    }

    private int CountProcessing() => _photos.Values.Count(p => p.Status is PhotoStatus.Processing);

    private static int BacklogDelta(ClaimPhoto? previous, ClaimPhoto? current) =>
        (current?.Status is PhotoStatus.Processing ? 1 : 0) - (previous?.Status is PhotoStatus.Processing ? 1 : 0);
}
