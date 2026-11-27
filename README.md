# Aspire Samples

[![CI (main)](https://github.com/dotnet/aspire-samples/actions/workflows/ci.yml/badge.svg)](https://github.com/dotnet/aspire-samples/actions/workflows/ci.yml)

Samples for [Aspire](https://aspire.dev).

[Aspire](https://aspire.dev) is a developer-first toolset that streamlines integrating front-ends, APIs, containers, and databases with your apps. [Learn more about Aspire here](https://aspire.dev/get-started/what-is-aspire/).

## Aspire version

These samples target the Aspire **17.0.0** CLI and SDK.
The repository's [`nuget.config`](./nuget.config) restores packages from NuGet.org.
The Java hosting and PostgreSQL Entity Framework Core integrations don't have a stable 17.0.0 package yet,
so they pin the preview package `17.0.0-preview.1.26567.5` alongside this release.

Install the matching Aspire CLI before building or running the samples:

```bash
curl -fsSL https://aspire.dev/install.sh | bash -s -- --version 17.0.0
```

On Windows, use PowerShell:

```powershell
& ([scriptblock]::Create((Invoke-RestMethod https://aspire.dev/install.ps1))) -Version 17.0.0
```

Dev Containers and CI install the same CLI version automatically.

## Browse by scenario

- [Full-stack JavaScript / TypeScript](#full-stack-javascript--typescript)
- [Polyglot full-stack](#polyglot-full-stack)
- [Backend integrations by language](#backend-integrations-by-language)
- [Interactive terminals](#interactive-terminals)
- [Cloud / AI / event-driven](#cloud--ai--event-driven)
- [.NET + frontend and platform](#net--frontend-and-platform)

The **Highlights** column lists what each sample shows off in the Aspire dashboard. See [CONTRIBUTING.md](./CONTRIBUTING.md) for the quality bar behind it.

### Full-stack JavaScript / TypeScript

| Sample | Workload languages | AppHost | Deploy | Description | Highlights |
| --- | --- | --- | --- | --- | --- |
| [Integrating Frontend Apps](./samples/aspire-with-javascript) | JavaScript, TypeScript | C# AppHost | Run only | Angular, React, Vue, and Vite shipment-tracking boards on one .NET API. | Browser-to-API traces, custom metrics, Scalar, dashboard commands |
| [Node + Redis + Vite](./samples/node-express-redis) | JavaScript, TypeScript | TypeScript AppHost | Docker Compose | Shopping cart service: Express API with Redis-backed carts and a React storefront behind YARP. | Browser-to-Redis traces, span links, pino structured logs, Scalar |
| [Node.js Fleet Weather Risk](./samples/aspire-with-node) | JavaScript, TypeScript | TypeScript AppHost | Run only | Dispatch board that scores delivery-route weather risk from the Open-Meteo external service. | Browser-to-external-service traces, custom metrics, Scalar, dashboard commands |
| [Vite + YARP Static Files](./samples/vite-yarp-static) | JavaScript, TypeScript | TypeScript AppHost | Docker Compose | Vite frontend served through YARP in run and publish modes. | YARP static files, Docker Compose publish |

### Polyglot full-stack

| Sample | Workload languages | AppHost | Deploy | Description | Highlights |
| --- | --- | --- | --- | --- | --- |
| [Integrating Python Apps](./samples/aspire-with-python) | Python, JavaScript | C# AppHost | Run only | Inventory availability: FastAPI with Redis cache-aside and a React frontend. | Browser-to-Redis traces, cache hit/miss metrics, Scalar, dashboard commands |
| [Vite + C# + PostgreSQL](./samples/vite-csharp-postgres) | C#, JavaScript, TypeScript | TypeScript AppHost | Docker Compose | IT service desk queue: React frontend, C# API, and PostgreSQL. | Browser-to-PostgreSQL traces, SLA metrics, Scalar, dashboard commands |
| [Vite + React + FastAPI](./samples/vite-react-fastapi) | Python, JavaScript, TypeScript | TypeScript AppHost | Docker Compose | Team task board: React frontend and FastAPI behind YARP. | Browser-to-API traces, custom metrics, Scalar, dashboard commands |
| [Spring Petclinic](./samples/spring-petclinic) | Java, TypeScript | TypeScript AppHost | Run only | Clinic front desk: an Angular app books visits through a Spring Boot API on PostgreSQL, using the official Java hosting integration. | Browser-to-PostgreSQL traces, Java agent metrics, springdoc Scalar, dashboard commands |
| [Polyglot Task Queue](./samples/polyglot-task-queue) | JavaScript, Python, C# | TypeScript AppHost | Docker Compose | Back-office document pipeline: a React board and Node API route work over RabbitMQ to Python, C#, and Node.js workers. | One trace across RabbitMQ and three languages, per-worker metrics, Scalar, dashboard commands |
| [RAG Document Q&A](./samples/rag-document-qa-svelte) | Python, JavaScript | TypeScript AppHost | Run only | Employee handbook Q&A with cited answers: Svelte frontend, FastAPI, Qdrant, and OpenAI. | GenAI telemetry, browser traces, Scalar |

### Backend integrations by language

| Sample | Workload languages | AppHost | Deploy | Description | Highlights |
| --- | --- | --- | --- | --- | --- |
| [Integrating a Go App](./samples/container-build) | Go | C# AppHost | Docker Compose | Go Gin feature-flag service built from a Dockerfile: hot reload in run mode, distroless image for publish. | Dockerfile build, rollout gauge, otelslog logs, Scalar |
| [Custom MailDev and MailKit integrations](./samples/maildev-mailkit) | C#, TypeScript | TypeScript AppHost | Run only | Custom hosting and client integrations with ATS-generated TypeScript APIs, sending transactional email. | Custom integrations, email traces, Scalar, dashboard commands |
| [Go API](./samples/golang-api) | Go | Go AppHost | Docker Compose | Field-service work-order API built with Go and chi. | Go AppHost, otelhttp traces, otelslog logs, Scalar |
| [Python FastAPI + PostgreSQL](./samples/python-fastapi-postgres) | Python | TypeScript AppHost | Docker Compose | Customer CRM API: FastAPI and PostgreSQL with pgAdmin. | SQL spans, problem details, Scalar, dashboard commands |
| [Python OpenAI Agent](./samples/python-openai-agent) | Python | TypeScript AppHost | Docker Compose | Order-support AI agent built on FastAPI and OpenAI tool calling. | GenAI telemetry, tool-call traces, Scalar |
| [Python Script](./samples/python-script) | Python | TypeScript AppHost | Run only | Minimal Python script sample using a file-based AppHost. | Python script resource |

### Interactive terminals

| Sample | Workload languages | AppHost | Deploy | Description | Highlights |
| --- | --- | --- | --- | --- | --- |
| [Terminal basics: C#](./samples/terminals/basics-csharp) | C# | C# AppHost | Run only | Slumber REST TUI + notes API + Redis REPL using Aspire 17.0. | Resource terminal, docked REPL, Scalar, dashboard commands |
| [Terminal basics: TypeScript](./samples/terminals/basics-typescript) | TypeScript | TypeScript AppHost | Run only | Slumber REST TUI + notes API + Redis REPL using Aspire 17.0. | Resource terminal, docked REPL, pino structured logs, Scalar |
| [Terminal automation](./samples/terminals/automation-csharp) | C# | C# AppHost | Run only | Automate Slumber CRUD with terminal input, screen assertions, and API verification using Aspire 17.0. | Terminal automation, correlated logs and traces |
| [Custom docked SQL REPL](./samples/terminals/docked-repl-csharp) | C# | C# AppHost | Run only | Custom rqlite resource with an on-demand docked SQL shell using Aspire 17.0. | Docked terminal command |

### Cloud / AI / event-driven

| Sample | Workload languages | AppHost | Deploy | Description | Highlights |
| --- | --- | --- | --- | --- | --- |
| [Azure Functions](./samples/aspire-with-azure-functions) | C# | C# AppHost | Azure | Insurance claim photo intake: Blazor front end, Blob Storage, and a blob-triggered Azure Function. | One trace per upload, custom metrics, Scalar, dashboard commands |
| [Image Gallery](./samples/image-gallery) | C#, JavaScript, TypeScript | C# AppHost | Azure | Product-photo pipeline: Blob Storage, SQL, and queue-triggered thumbnails in Container Apps Jobs. | API-to-queue-to-job traces, custom metrics, Scalar |
| [Custom Metrics Visualization](./samples/Metrics) | C# | C# AppHost | Run only | Checkout service metrics flowing through an OpenTelemetry Collector to Prometheus and Grafana. | Grafana dashboard, exemplars, Scalar, dashboard commands |
| [Standalone Aspire dashboard](./samples/standalone-dashboard) | C# | C# AppHost | Run only | Runs the Aspire dashboard container against any OpenTelemetry source. | Standalone dashboard |

### .NET + frontend and platform

| Sample | Workload languages | AppHost | Deploy | Description | Highlights |
| --- | --- | --- | --- | --- | --- |
| [Aspire Shop](./samples/aspire-shop) | C# | C# AppHost | Run only | Blazor storefront with a PostgreSQL catalog API and a Redis-backed gRPC basket service. | gRPC traces, custom metrics, Scalar, dashboard commands |
| [HealthChecksUI](./samples/health-checks-ui) | C# | C# AppHost | Docker Compose | Payments gateway with per-dependency health checks and the HealthChecksUI container. | Degraded health states, Scalar, dashboard commands |
| [Integrating Client Apps](./samples/client-apps-integration) | C# | C# AppHost | Run only | Integrates Windows Forms and WPF apps with Aspire. | Desktop app telemetry |
| [Integrating Orleans](./samples/orleans-voting) | C# | C# AppHost | Run only | Event box office: one grain per event serializes seat reservations on a three-silo cluster. | Cross-silo traces, contention metrics, Scalar, dashboard commands |
| [Working with Database Containers](./samples/database-containers) | C#, SQL | C# AppHost | Run only | PostgreSQL, MySQL, and SQL Server containers seeded from init scripts and used by one orders API. | Init scripts, three-database traces, Scalar, dashboard commands |
| [Running EF Core Migrations](./samples/database-migrations) | C# | C# AppHost | Run only | A migration worker brings a clinic appointments schema up to date before the API starts. | WaitForCompletion, migration spans, Scalar, dashboard commands |
| [Persisting Data with Volume Mounts](./samples/volume-mount) | C# | C# AppHost | Run only | Equipment inspections stored in SQL Server and Azure Blob Storage on persistent Docker volumes. | Persistent volumes, Scalar, dashboard commands |
| [Custom Aspire hosting resources](./samples/custom-resources) | C# | C# AppHost | Run only | Demonstrates authoring custom hosting resources with Aspire. | Custom resource states |

## eShop

[eShop](https://github.com/dotnet/eshop) is a reference application implementing an eCommerce web site on a services-based architecture using Aspire.

## Aspire Links

- [Aspire Documentation](https://aspire.dev/docs/)
- [Aspire Blog](https://devblogs.microsoft.com/aspire/)
- [Aspire GitHub](https://github.com/dotnet/aspire)

## License

These samples are licensed under the [MIT license](./LICENSE).

## Disclaimer

The sample applications provided in this repository are intended to illustrate individual concepts that may be beneficial in understanding the underlying technology and its potential uses. These samples may not illustrate best practices for production environments.

The code is not intended for operational deployment. Users should exercise caution and not rely on the samples as a foundation for any commercial or production use.

See the following links for more information on best practices and security considerations when hosting applications:

- [ASP.NET Core security topics](https://learn.microsoft.com/aspnet/core/security/)
- [Node.js security best practices](https://nodejs.org/en/learn/getting-started/security-best-practices)
- [FastAPI security](https://fastapi.tiangolo.com/tutorial/security/)

## Contributing

We welcome contributions to this repository of samples related to official Aspire features and integrations (i.e. those pieces whose code lives in the [Aspire repo](https://github.com/dotnet/aspire) and that ship from the [**Aspire** NuGet account](https://www.nuget.org/profiles/aspire)). It's generally a good idea to [log an issue](https://github.com/dotnet/aspire-samples/issues/new/choose) first to discuss any idea for a sample with the team before sending a pull request.

## Code of conduct

This project has adopted the code of conduct defined by the [Contributor Covenant](https://contributor-covenant.org) to clarify expected behavior in our community. For more information, see the [.NET Foundation Code of Conduct](https://www.dotnetfoundation.org/code-of-conduct).

## Using Devcontainer and Codespaces

This repository includes a devcontainer configuration to help you quickly set up a development environment using Visual Studio Code and GitHub Codespaces.

### Setting up Devcontainer

1. **Install Visual Studio Code**: If you haven't already, download and install [Visual Studio Code](https://code.visualstudio.com/).

2. **Install Dev Containers extension**: Open Visual Studio Code and go to the Extensions view by clicking on the Extensions icon in the Activity Bar on the side of the window. Search for "Dev Containers" and install the extension.

3. **Clone the repository**: Clone this repository to your local machine.

4. **Open the repository in Visual Studio Code**: Open Visual Studio Code and use the `File > Open Folder` menu to open the folder where you cloned this repository.

5. **Reopen in Container**: Once the repository is open, you should see a notification prompting you to reopen the folder in a container. Click the "Reopen in Container" button. If you don't see the notification, you can also use the `Remote-Containers: Reopen in Container` command from the Command Palette (Ctrl+Shift+P).

### Using GitHub Codespaces

1. **Open the repository on GitHub**: Navigate to this repository on GitHub.

2. **Create a Codespace**: Click the "Code" button and then click the "Open with Codespaces" tab. Click the "New codespace" button to create a new Codespace.

3. **Wait for the Codespace to start**: GitHub will set up a new Codespace with the devcontainer configuration defined in this repository. This may take a few minutes.

4. **Start coding**: Once the Codespace is ready, you can start coding directly in your browser or open the Codespace in Visual Studio Code.

The devcontainer configuration includes all the necessary tools and dependencies to run the samples in this repository. You can start coding and running the samples without having to install anything else on your local machine.
