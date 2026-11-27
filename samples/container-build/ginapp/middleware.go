package main

import (
	"log/slog"
	"net/http"
	"os"
	"strings"

	"github.com/gin-gonic/gin"
	"go.opentelemetry.io/contrib/instrumentation/github.com/gin-gonic/gin/otelgin"
)

// setupMiddleware adds tracing (server spans named "METHOD /route" plus HTTP server metrics), console access logs,
// and panic recovery that answers with problem details. Health probes aren't traced or logged.
func setupMiddleware(router *gin.Engine) {
	serviceName := os.Getenv("OTEL_SERVICE_NAME")
	if serviceName == "" {
		serviceName = "ginapp"
	}
	router.Use(otelgin.Middleware(serviceName, otelgin.WithGinFilter(func(c *gin.Context) bool { return c.FullPath() != "/health" })))
	router.Use(gin.LoggerWithConfig(gin.LoggerConfig{SkipPaths: []string{"/health"}}))
	router.Use(gin.CustomRecovery(func(c *gin.Context, _ any) {
		writeProblem(c, http.StatusInternalServerError, "An unexpected error occurred.", nil)
	}))
}

// configureTrustedProxies applies TRUSTED_PROXIES: "all", a semicolon-separated list of networks, or unset (none).
func configureTrustedProxies(router *gin.Engine, logger *slog.Logger) {
	switch trustedProxies := os.Getenv("TRUSTED_PROXIES"); trustedProxies {
	case "all":
		logger.Info("Trusting all proxy networks")
	case "":
		_ = router.SetTrustedProxies(nil)
	default:
		proxies := strings.Split(trustedProxies, ";")
		if err := router.SetTrustedProxies(proxies); err != nil {
			logger.Error("Invalid TRUSTED_PROXIES value", "error", err)
			os.Exit(1)
		}
		logger.Info("Configured trusted proxies", "proxies", proxies)
	}
}
