# Claim Photo Intake with Azure Functions

![Contoso Insurance claim photo intake page with eight damage photos across three claims, one flagged for a retake (light theme)](./images/aspire-with-azure-functions-app-light.png#gh-light-mode-only)
![Contoso Insurance claim photo intake page with eight damage photos across three claims, one flagged for a retake (dark theme)](./images/aspire-with-azure-functions-app-dark.png#gh-dark-mode-only)

**Contoso Insurance** adjusters and policyholders attach damage photos to open claims: a rear-end collision, a kitchen water leak and a hail-damaged roof. Each photo is stored in Azure Blob Storage, where a blob-triggered Azure Function checks that it's sharp enough to assess (adjusters need at least 720p), renders a thumbnail, and queues the result back to the intake page, which updates live. Photos below the minimum resolution are flagged **Retake requested**.

The app consists of two services:

- **ImageGallery.FrontEnd**: A Blazor app and minimal API for claim photo intake. It uploads photos to the `claim-photos` blob container, reads the function's results from the `thumbnail-queue` queue, and serves photos and thumbnails. It ships light and dark themes that follow `prefers-color-scheme`.
- **ImageGallery.Functions**: An Azure Functions (isolated worker) app. Its `ProcessClaimPhoto` function is triggered by every new blob in `claim-photos` using a Functions Blob Trigger. It checks the photo's resolution, writes a 480 px thumbnail to the `thumbnails` container, and sends the result to `thumbnail-queue`.

The app also includes a class library project, **ImageGallery.Shared**, with the storage layout and result message both services use, a class library project, **ImageGallery.ServiceDefaults**, that contains the service defaults used by the service projects, and the **ImageGallery.AppHost** Aspire App Host project. Locally, Aspire runs Azure Storage in the Azurite emulator and the function in the Azure Functions Core Tools; the App Host also targets Azure Container Apps for deployment.

One upload produces **one distributed trace** across the front end, the blob write, the function's processing and thumbnail write, and the front end handling the function's result. Blob triggers have no message headers, so the front end stores the W3C `traceparent` in the blob's metadata and the function continues that trace. The function then puts its own trace context in the result message.

## Pre-requisites

- [Aspire development environment](https://aspire.dev/get-started/prerequisites/)
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [Azure Functions Core Tools v4](https://learn.microsoft.com/azure/azure-functions/functions-run-local?tabs=windows%2Cisolated-process%2Cnode-v4%2Cpython-v2%2Chttp-trigger%2Ccontainer-apps&pivots=programming-language-csharp) (`func` on your `PATH`)
- A container runtime such as Docker Desktop, for the Azurite storage emulator

## Running the app

If using the Aspire CLI, run `aspire run` from this directory.

If using VS Code, open this directory as a workspace and launch the `ImageGallery.AppHost` project using either the Aspire or C# debuggers.

If using Visual Studio, open the solution file `ImageGallery.slnx` and launch/debug the `ImageGallery.AppHost` project.

If using the .NET CLI, run `dotnet run` from the `ImageGallery.AppHost` directory.

Open **Claim photo intake** from the dashboard, then run **Upload sample photos** on the `frontend` resource to attach eight damage photos (bundled in `ImageGallery.FrontEnd/SamplePhotos`) to the three open claims. You can also pick a claim and add your own JPEG, PNG or WebP photos.

## What to explore in the Aspire dashboard

### Resources and commands

![Aspire dashboard resources page with the frontend actions menu open and the Upload sample photos command's description shown (light theme)](./images/aspire-with-azure-functions-dashboard-resources-light.png#gh-light-mode-only)
![Aspire dashboard resources page with the frontend actions menu open and the Upload sample photos command's description shown (dark theme)](./images/aspire-with-azure-functions-dashboard-resources-dark.png#gh-dark-mode-only)

- **frontend** links to **Claim photo intake** and **API reference (Scalar)**; the **OpenAPI JSON** link is on its details page. It waits for **functions**, which waits for **storage** (Azurite) with its `blobs` and `queues` children.
- **Upload sample photos** (highlighted on the `frontend` row) calls the development-only `POST /api/operations/sample-photos`. Each photo goes through the same pipeline as a browser upload, so the intake page, traces and metrics fill up within seconds. Run it again to reprocess the same photos.
- **Delete all photos** asks for confirmation, then calls `DELETE /api/operations/photos` so the intake page starts empty.

### Traces

![Aspire dashboard trace of one claim photo upload spanning frontend, storage and functions, with the function's ProcessClaimPhoto span selected and its faas and claims attributes shown (light theme)](./images/aspire-with-azure-functions-trace-light.png#gh-light-mode-only)
![Aspire dashboard trace of one claim photo upload spanning frontend, storage and functions, with the function's ProcessClaimPhoto span selected and its faas and claims attributes shown (dark theme)](./images/aspire-with-azure-functions-trace-dark.png#gh-dark-mode-only)

Upload a photo (or call `POST /api/claims/{claimNumber}/photos`) and open its trace to follow it end to end:

- **frontend**: `POST /api/claims/{claimNumber}/photos` → `upload claim photo` (producer span with `claims.claim.number`, `claims.photo.name`, `claims.photo.size`, `claims.photo.format` and a `photo.stored` event) → `BlobClient.Upload`. Browser uploads start at `upload claim photo`.
- **functions**: `ProcessClaimPhoto` (consumer span parented on the upload through the `traceparent` blob metadata, with the [FaaS semantic conventions](https://opentelemetry.io/docs/specs/semconv/faas/) `faas.trigger`, `faas.invocation_id`, `faas.document.collection` and `faas.document.name`) → `BlobBaseClient.DownloadContent` → `render thumbnail` (source and thumbnail dimensions plus `image.decoded` and `thumbnail.encoded` events) → `BlobClient.Upload` of the thumbnail → `QueueClient.SendMessage`. Low-resolution photos get a `photo.retake_requested` event.
- **frontend**: `process thumbnail-queue` (consumer span parented on the function's span through the result message, with `messaging.*` attributes and the photo's `claims.photo.turnaround_ms`) → `QueueClient.DeleteMessage`.
- The gap between `BlobClient.Upload` and `ProcessClaimPhoto` is the blob trigger's pickup time. The Functions host records its own `ProcessClaimPhoto` invocation span in a separate trace; the consumer span links to it.
- The front end polls the results queue every second; a custom sampler drops those idle root polling spans so the trace list only shows real work.

### Metrics

![Aspire dashboard metrics page charting claims.photo.processing.duration percentiles for the Contoso.PhotoProcessor meter (light theme)](./images/aspire-with-azure-functions-metrics-light.png#gh-light-mode-only)
![Aspire dashboard metrics page charting claims.photo.processing.duration percentiles for the Contoso.PhotoProcessor meter (dark theme)](./images/aspire-with-azure-functions-metrics-dark.png#gh-dark-mode-only)

| Meter | Instrument | Description |
| --- | --- | --- |
| `Contoso.ClaimIntake` | `claims.photos.uploaded` | Photos stored in Blob Storage, by `claims.photo.source` (`browser`, `api` or `sample`) and `claims.photo.format` |
| `Contoso.ClaimIntake` | `claims.photo.upload.size` | Histogram of accepted upload sizes, in bytes |
| `Contoso.ClaimIntake` | `claims.photos.rejected` | Uploads rejected before reaching storage, by `claims.rejection.reason` (`unknown_claim`, `empty`, `too_large`, `unsupported_type`) |
| `Contoso.ClaimIntake` | `claims.photos.backlog` | Up-down counter of uploaded photos still waiting for the function's result |
| `Contoso.ClaimIntake` | `claims.photo.turnaround` | Histogram of the time from upload to the function's result reaching the front end, including the blob trigger's pickup time, in seconds |
| `Contoso.ClaimIntake` | `claims.photos.retriggered` | Photos the blob trigger hadn't picked up after 30 seconds (see [Notes](#notes)) |
| `Contoso.PhotoProcessor` | `claims.photos.processed` | Photos checked and thumbnailed, by `claims.photo.quality` (`ok` or `low_resolution`) and `claims.photo.format` |
| `Contoso.PhotoProcessor` | `claims.photo.processing.duration` | Histogram of the time to download, check, resize and save a thumbnail and queue the result, in seconds |
| `Contoso.PhotoProcessor` | `claims.photos.failed` | Photos the function couldn't process, by `error.type` (`unreadable_image`, `storage_error`) |
| `Contoso.PhotoProcessor` | `claims.photos.processing.active` | Up-down counter of photos the function is processing right now |

Metric tags are bounded (sources, formats, reasons, qualities and error types). Claim numbers and file names only appear on spans and logs.

### Structured logs

![Aspire dashboard structured logs filtered to claim CLM-26-10533, showing upload, processing, retake and result entries from frontend and functions, with the retake warning's structured fields open (light theme)](./images/aspire-with-azure-functions-logs-light.png#gh-light-mode-only)
![Aspire dashboard structured logs filtered to claim CLM-26-10533, showing upload, processing, retake and result entries from frontend and functions, with the retake warning's structured fields open (dark theme)](./images/aspire-with-azure-functions-logs-dark.png#gh-dark-mode-only)

Both services log through source-generated `[LoggerMessage]` methods (`ImageGallery.FrontEnd/Log.cs` and `ImageGallery.Functions/Log.cs`), so fields such as `ClaimNumber`, `FileName`, `SizeBytes`, `Source`, `Width`, `Height`, `Quality`, `ElapsedMs`, `TurnaroundMs` and `MessageId` are filterable in **Structured logs**, and every entry links to its trace. Filter on `ClaimNumber` to see a claim's uploads, the function's results and the `Retake requested` warning side by side. The Functions host also forwards the worker's logs under the `Function.ProcessClaimPhoto.User` category, attached to the host's invocation trace.

### API reference (Scalar)

![Scalar API reference open on Upload a damage photo, showing the multipart body, the 202, 400, 404 and 413 responses and a typed example (light theme)](./images/aspire-with-azure-functions-scalar-light.png#gh-light-mode-only)
![Scalar API reference open on Upload a damage photo, showing the multipart body, the 202, 400, 404 and 413 responses and a typed example (dark theme)](./images/aspire-with-azure-functions-scalar-dark.png#gh-dark-mode-only)

Open **API reference (Scalar)** on the `frontend` resource (served at `/scalar` in Development; the raw document is at `/openapi/v1.json`). The "Contoso Claim Photo Intake API" document groups operations into **Claims**, **Photos** and **Operations** tags; every operation has a summary, a description, typed responses and [RFC 9457](https://www.rfc-editor.org/rfc/rfc9457) problem details:

| Method | Route | Description |
| --- | --- | --- |
| `GET` | `/api/claims` | List open claims with photo counts |
| `GET` | `/api/claims/{claimNumber}/photos` | List a claim's photos and their review status |
| `POST` | `/api/claims/{claimNumber}/photos` | Upload a damage photo (multipart, max 10 MB); `202` while the function processes it, `400`, `404` or `413` problem details otherwise |
| `GET` | `/api/claims/{claimNumber}/photos/{fileName}` | Download the original photo |
| `GET` | `/api/claims/{claimNumber}/photos/{fileName}/thumbnail` | Download the 480 px thumbnail (`404` until it's ready) |
| `POST` | `/api/operations/sample-photos` | Development only: upload the bundled sample photos |
| `DELETE` | `/api/operations/photos` | Development only: delete every photo and thumbnail |

## Notes

- The Functions Blob Trigger polls the container, so a photo usually takes a second or two to be picked up; in Azure, use the [Event Grid-based blob trigger](https://learn.microsoft.com/azure/azure-functions/functions-event-grid-blob-trigger) for lower latency at scale.
- The container scan in the current blob extension can skip a blob that's uploaded while a scan is running ([Azure/azure-sdk-for-net#62763](https://github.com/Azure/azure-sdk-for-net/issues/62763)). As a safety net, the front end rewrites the metadata of any photo still waiting after 30 seconds, which gives it a new ETag so the next scan picks it up, and counts it in `claims.photos.retriggered`.
- The upload API has no authentication; add it before exposing the app.
