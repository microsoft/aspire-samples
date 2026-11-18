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

	// Publish the dashboard on a fixed host port so its URL doesn't change between deployments.
	env := builder.AddDockerComposeEnvironment("env").
		ConfigureDashboard(func(dashboard aspire.DockerComposeAspireDashboardResource) {
			if err := dashboard.WithHostPort(&aspire.WithHostPortOptions{Port: aspire.IntPtr(9003)}).Err(); err != nil {
				log.Fatal(aspire.FormatError(err))
			}
		})

	// AddGoApp runs `go run .` from ./api during `aspire run`. When publishing, it builds the
	// image from the Dockerfile that is checked in next to the API instead of generating one.
	api := builder.AddGoApp("api", "./api").
		WithModTidy().
		WithHttpEndpoint(&aspire.WithHttpEndpointOptions{Env: aspire.StringPtr("PORT")}).
		WithHttpHealthCheck(&aspire.WithHttpHealthCheckOptions{Path: aspire.StringPtr("/health")}).
		WithExternalHttpEndpoints()

	// Each call records its failure on the handle that it returns, so check every resource.
	for _, resource := range []aspire.Resource{env, api} {
		if err := resource.Err(); err != nil {
			log.Fatal(aspire.FormatError(err))
		}
	}

	app, err := builder.Build()
	if err != nil {
		log.Fatal(aspire.FormatError(err))
	}

	if err := app.Run(); err != nil {
		log.Fatal(aspire.FormatError(err))
	}
}
