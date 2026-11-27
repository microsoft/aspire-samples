package main

import (
	"log"

	"apphost/modules/aspire"
)

func main() {
	builder, err := aspire.CreateBuilder()
	if err != nil {
		log.Fatal(aspire.FormatError(err))
	}

	isRunMode, err := builder.ExecutionContext().IsRunMode()
	if err != nil {
		log.Fatal(aspire.FormatError(err))
	}

	// Publish the dashboard on a fixed host port so its URL doesn't change between deployments.
	builder.AddDockerComposeEnvironment("env").
		ConfigureDashboard(func(dashboard aspire.DockerComposeAspireDashboardResource) {
			dashboard.WithHostPort(&aspire.WithHostPortOptions{Port: aspire.IntPtr(9003)})
		})

	// AddGoApp runs `go run .` from ./api during `aspire run`. When publishing, it builds the
	// image from the Dockerfile that is checked in next to the API instead of generating one.
	api := builder.AddGoApp("api", "./api").
		WithModTidy().
		WithHttpEndpoint(&aspire.WithHttpEndpointOptions{Env: aspire.StringPtr("PORT")}).
		WithHttpHealthCheck(&aspire.WithHttpHealthCheckOptions{Path: aspire.StringPtr("/health")}).
		WithExternalHttpEndpoints()

	endpoint := api.GetEndpoint("http")
	api.WithUrl(aspire.RefExpr("%v/scalar", endpoint), aspire.UrlDisplayText("API reference (Scalar)")).
		WithUrl(aspire.RefExpr("%v/openapi.json", endpoint), aspire.UrlDisplayText("OpenAPI JSON"))

	// The seed and simulate endpoints only exist when WORKORDERS_ADMIN_ENABLED is set, so published
	// deployments never expose them.
	if isRunMode {
		api.WithEnvironment("WORKORDERS_ADMIN_ENABLED", "true").
			WithHttpCommand("/admin/simulate", "Simulate traffic", aspire.HttpCommandOptions(&aspire.HttpCommandExportOptions{
				CommandName:   aspire.StringPtr("simulate-traffic"),
				Description:   aspire.StringPtr("Open, dispatch, and complete work orders through the API, including a few rejected requests."),
				IconName:      aspire.StringPtr("PlayCircle"),
				IsHighlighted: true,
				MethodName:    aspire.StringPtr("POST"),
				ResultMode:    aspire.HttpCommandResultModeJson,
			})).
			WithHttpCommand("/admin/seed", "Seed work orders", aspire.HttpCommandOptions(&aspire.HttpCommandExportOptions{
				CommandName:         aspire.StringPtr("seed-work-orders"),
				Description:         aspire.StringPtr("Replace all work orders with a 12-job sample backlog."),
				ConfirmationMessage: aspire.StringPtr("Replace all work orders with the sample backlog?"),
				IconName:            aspire.StringPtr("DatabaseArrowUp"),
				MethodName:          aspire.StringPtr("POST"),
				ResultMode:          aspire.HttpCommandResultModeJson,
			}))
	}

	app, err := builder.Build()
	if err != nil {
		log.Fatal(aspire.FormatError(err))
	}

	if err := app.Run(); err != nil {
		log.Fatal(aspire.FormatError(err))
	}
}
