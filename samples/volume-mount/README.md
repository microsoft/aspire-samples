# Persistent Volume

This sample shows how to keep container data across restarts in Aspire by giving each container a Docker **data volume**. The app is **FieldCheck**, a small equipment-inspection tracker for facilities teams:

- Inspection records (asset, site, inspector, outcome, findings) are stored in **SQL Server**. `WithDataVolume()` mounts a named volume at `/var/opt/mssql`, so the database files live outside the container.
- Photos and documents attached to an inspection are stored in **Azure Blob Storage**. Locally that's the Azurite emulator, and its data volume (mounted at `/data`) keeps the uploaded blobs.

Stop the app, restart the `sqlserver` container, or delete and re-create it, and your inspections and their attachments are still there. The same `WithDataVolume()` pattern works for other containers too, such as PostgreSQL and Redis.

![FieldCheck home page showing SQL Server and Blob Storage volume cards and a grid of inspections with nameplate photos (light theme)](./images/volume-mount-app-light.png#gh-light-mode-only)
![FieldCheck home page showing SQL Server and Blob Storage volume cards and a grid of inspections with nameplate photos (dark theme)](./images/volume-mount-app-dark.png#gh-dark-mode-only)

The solution contains:

- **VolumeMount.AppHost**: the Aspire AppHost. It defines SQL Server and Azure Storage with data volumes, plus the dashboard commands and URLs.
- **VolumeMount.BlazorWeb**: a Blazor (static SSR) app and minimal API for recording and browsing inspections. It has OpenAPI and a Scalar API reference in Development.
- **VolumeMount.Data**: the Entity Framework Core model (`Inspection`, `InspectionAttachment`) and migrations.
- **VolumeMount.MigrationService**: a worker that applies pending migrations before the web app starts.
- **VolumeMount.ServiceDefaults**: shared OpenTelemetry, health check, and resilience defaults.

## Pre-requisites

- [Aspire development environment](https://aspire.dev/get-started/prerequisites/)
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)

## Running the app

1. If using the Aspire CLI, run `aspire run` from this directory.

   If using VS Code, open this directory as a workspace and launch the `VolumeMount.AppHost` project using either the Aspire or C# debuggers.

   If using Visual Studio, open the solution file `VolumeMount.slnx` and launch/debug the `VolumeMount.AppHost` project.

   If using the .NET CLI, run `dotnet run` from the `VolumeMount.AppHost` directory.

1. In the Aspire dashboard, open the **blazorweb** resource's actions menu and run **Seed sample data**. It creates a week of inspections across several sites, each with a nameplate photo and a CSV of readings. You can also click **Seed sample data** on the app's empty home page.

1. Open **Inspections app (https)** from the dashboard. Pick **New inspection** to record your own visit and attach photos or documents. The record goes to SQL Server and the files go to Blob Storage, all in one traced operation.

   ![Inspection detail page with the inspector, findings, SQL table, and attachments stored in Blob Storage (light theme)](./images/volume-mount-app-detail-light.png#gh-light-mode-only)
   ![Inspection detail page with the inspector, findings, SQL table, and attachments stored in Blob Storage (dark theme)](./images/volume-mount-app-detail-dark.png#gh-dark-mode-only)

### Prove the data persists

1. Note the inspection count and the **Server started** time on the SQL Server card on the home page. You can also run **Check persisted data** from the **blazorweb** actions menu, which returns the counts and `sqlServerStartedAt` as JSON.
1. In the dashboard, restart the **sqlserver** resource with **Restart** in its actions menu. Aspire re-creates the container and mounts the same volume.
1. Refresh the app or run **Check persisted data** again. **Server started** has changed, but every inspection and attachment is still there.
1. Stop the whole app and run it again. The data is still there, including the blobs in Azurite.

The named volumes are visible from the Docker CLI:

```shell
> docker volume ls -f name=volumemount
DRIVER    VOLUME NAME
local     volumemount.apphost-a06e86665e-sqlserver-data
local     volumemount.apphost-a06e86665e-storage-data
```

To start from scratch, stop the app, remove the `sqlserver-*` container (it uses a persistent lifetime), and then run `docker volume rm` on both volumes.

## What to explore in the Aspire dashboard

### Resources and commands

- **sqlserver** (SQL Server, persistent lifetime) with the **sqldb** database, and **storage** (the Azurite emulator) with the **storage-blobs** service and the **attachments** blob container (`inspection-attachments`).
- The **blazorweb** actions menu has two commands:
  - **Seed sample data** (`POST /api/dev/seed`, highlighted).
  - **Check persisted data** (`GET /api/storage/status`).

  Both show their JSON result in the dashboard.
- The **blazorweb** URLs include **API reference (Scalar)**. The resource details also list **OpenAPI JSON**.

![Aspire dashboard resources with the blazorweb actions menu open on Seed sample data and Check persisted data (light theme)](./images/volume-mount-dashboard-resources-light.png#gh-light-mode-only)
![Aspire dashboard resources with the blazorweb actions menu open on Seed sample data and Check persisted data (dark theme)](./images/volume-mount-dashboard-resources-dark.png#gh-dark-mode-only)

Select **sqlserver** to open its details. The **Volumes** card shows the `volumemount.apphost-…-sqlserver-data` volume mounted at `/var/opt/mssql`. **Container lifetime** is **Persistent**. **storage** shows its own volume mounted at `/data` in the same place.

![sqlserver resource details with the Volumes card showing the data volume mounted at /var/opt/mssql (light theme)](./images/volume-mount-dashboard-volume-light.png#gh-light-mode-only)
![sqlserver resource details with the Volumes card showing the data volume mounted at /var/opt/mssql (dark theme)](./images/volume-mount-dashboard-volume-dark.png#gh-dark-mode-only)

### Traces

Recording an inspection creates an `inspection.create` span from the `VolumeMount.Inspections` activity source. Under it are a `BlobClient.Upload` span (and its HTTP `PUT` to **storage**) for each attachment, followed by the `INSERT` statements against **sqldb**. The span carries the `inspection.asset_tag`, `inspection.site`, `inspection.outcome`, `inspection.source` (`ui`, `api`, or `seed`), `inspection.attachment.count`, and `inspection.id` attributes, plus `attachment.uploaded` and `inspection.saved` events. If the database save fails, the uploaded blobs are deleted so nothing is left orphaned.

![Trace for a new inspection: inspection.create spanning two Blob Storage uploads and the SQL Server inserts, with the span attributes (light theme)](./images/volume-mount-trace-light.png#gh-light-mode-only)
![Trace for a new inspection: inspection.create spanning two Blob Storage uploads and the SQL Server inserts, with the span attributes (dark theme)](./images/volume-mount-trace-dark.png#gh-dark-mode-only)

The migration service emits a `Migrating database` span with the number of migrations applied and the number of inspections already on the volume.

### Metrics

On the **blazorweb** resource, the `VolumeMount.Inspections` meter has these instruments:

| Instrument | Type | Tags |
| --- | --- | --- |
| `inspections.records.created` | Counter | `inspection.outcome`, `inspection.source` |
| `inspections.attachment.size` | Histogram (bytes) | `attachment.kind` (`image`, `document`) |
| `inspections.attachment.uploads.failed` | Counter | `attachment.kind`, `error.type` |
| `inspections.attachment.uploads.active` | UpDownCounter | |

![inspections.attachment.size histogram for blazorweb with P50, P90, and P99 lines and exemplars (light theme)](./images/volume-mount-metrics-light.png#gh-light-mode-only)
![inspections.attachment.size histogram for blazorweb with P50, P90, and P99 lines and exemplars (dark theme)](./images/volume-mount-metrics-dark.png#gh-dark-mode-only)

### Structured logs

The logs come from `[LoggerMessage]` source-generated methods, so each value is a structured field you can filter on:

- `Inspection {InspectionReference} saved for {AssetTag} at {Site}: {Outcome} with {AttachmentCount} attachments`
- `Uploaded {FileName} to blob {BlobName} ({SizeBytes} bytes)` (Debug) and `Failed to upload {FileName} to blob {BlobName}`
- `Storage status: {InspectionCount} inspections in SQL Server (started {SqlServerStartedAt}), {BlobCount} blobs in {BlobContainer}`
- From **migration**: `Applied {MigrationCount} pending migrations; {InspectionCount} inspections already persisted on the data volume`

### API reference

Open **API reference (Scalar)** from the **blazorweb** URLs. The API includes:

- `GET /api/inspections` and `GET /api/inspections/{id}`
- `POST /api/inspections` (multipart form with attachments)
- `GET /api/attachments/{id}/content`
- `GET /api/storage/status`
- `POST /api/dev/seed` (Development only)

Every endpoint has a summary, description, and typed responses.

![Scalar API reference showing the Record an inspection multipart operation with its schema and a curl example (light theme)](./images/volume-mount-scalar-light.png#gh-light-mode-only)
![Scalar API reference showing the Record an inspection multipart operation with its schema and a curl example (dark theme)](./images/volume-mount-scalar-dark.png#gh-dark-mode-only)
