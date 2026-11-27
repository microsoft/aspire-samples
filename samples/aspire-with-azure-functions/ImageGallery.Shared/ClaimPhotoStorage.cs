namespace ImageGallery.Shared;

// Storage layout shared by the front end (uploads, results) and the function (blob trigger, thumbnails).
// Photos are stored as "{claimNumber}/{fileName}" in both containers.
public static class ClaimPhotoStorage
{
    public const string PhotosContainer = "claim-photos";
    public const string ThumbnailsContainer = "thumbnails";
    public const string ResultsQueue = "thumbnail-queue";

    // Blob metadata keys. Azure Storage has no message headers for blob triggers, so the uploader's
    // W3C trace context travels in the photo's metadata.
    public static class Metadata
    {
        public const string ClaimNumber = "claimnumber";
        public const string OriginalFileName = "originalfilename";
        public const string Source = "source";
        public const string TraceParent = "traceparent";
        public const string TraceState = "tracestate";

        public const string Width = "width";
        public const string Height = "height";
        public const string Quality = "quality";
    }
}
