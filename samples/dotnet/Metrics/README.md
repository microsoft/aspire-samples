# Aspire metrics sample app

This is a simple .NET app that shows off collecting metrics with OpenTelemetry and exporting them to Prometheus and Grafana for reporting.

![Screenshot of the ASP.NET Core Grafana dashboard](./images/dashboard-screenshot.png)

The instrumented app is a small web UI that emits OpenTelemetry metrics. Click around it — the **Weather** feed and the **Auth Required** flow — to generate the request traffic that shows up on the Grafana dashboard above. It supports both light and dark themes:

![The metrics app home page in light mode](./images/metricsapp-light.png#gh-light-mode-only)
![The metrics app home page in dark mode](./images/metricsapp-dark.png#gh-dark-mode-only)

![The metrics app weather feed in light mode](./images/metricsapp-weather-light.png#gh-light-mode-only)
![The metrics app weather feed in dark mode](./images/metricsapp-weather-dark.png#gh-dark-mode-only)


## Demonstrates

- How to configure an Aspire app to export metrics to Prometheus
- How to add Prometheus and Grafana containers to an Aspire app
- How to configure Prometheus and Grafana to collect and display metrics in the [.NET Grafana dashboard](https://aka.ms/dotnet/grafana-source)

## Sample prerequisites

- [Aspire development environment](https://aspire.dev/get-started/prerequisites/)
- This sample is written in C# and targets .NET 10. It requires the [.NET 10.0 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) or later.

## Running the sample

If using the Aspire CLI, run `aspire run` from this directory.

If using VS Code, open this directory as a workspace and launch the `MetricsApp.AppHost` project using either the Aspire or C# debuggers.

If using Visual Studio, open the solution file `Metrics.slnx` and launch/debug the `MetricsApp.AppHost` project.

If using the .NET CLI, run `dotnet run` from the `MetricsApp.AppHost` directory.

1. On the **Resources** page, click the URLsfor the instrumented app. This launches the simple .NET app.
1. In the instrumented app:
   1. Visit the **Weather** and **Auth Required** pages to generate metrics. Values will be captured for `http.server.request.duration` and other instruments.
   1. On the **Home** page, click the Grafana dashboard link. This launches the ASP.NET Core dashboard in Grafana.
1. Play around inside the Grafana dashboard:
   1. Change the time range.
   1. Enable auto-refresh.
   1. Click route links to view detailed information about specific areas in the ASP.NET Core app.

For more information about using Grafana dashboards, see the [Grafana documentation](https://grafana.com/docs/grafana/latest/dashboards/use-dashboards/).
