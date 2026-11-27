package main

import (
	"context"
	"errors"
	"fmt"
	"log/slog"
	"net/http"
	"os"
	"os/signal"
	"strconv"
	"strings"
	"syscall"
	"time"

	"github.com/gin-gonic/gin"
)

func main() {
	ctx, stop := signal.NotifyContext(context.Background(), syscall.SIGINT, syscall.SIGTERM)
	defer stop()

	logger, shutdownTelemetry, err := setupTelemetry(ctx)
	if err != nil {
		logger = slog.New(slog.NewTextHandler(os.Stdout, nil))
		logger.Warn("Continuing without OpenTelemetry: the exporters couldn't be created", "error", err)
		shutdownTelemetry = func(context.Context) error { return nil }
	}
	slog.SetDefault(logger)

	port, err := strconv.Atoi(os.Getenv("PORT"))
	if err != nil {
		logger.Error("Set the PORT environment variable to the port to listen on", "PORT", os.Getenv("PORT"))
		os.Exit(1)
	}

	store := NewFlagStore(seedFlags)
	metrics, err := newFlagMetrics(store)
	if err != nil {
		logger.Error("Failed to create the Contoso.FeatureFlags instruments", "error", err)
		os.Exit(1)
	}
	api := &flagAPI{store: store, metrics: metrics, logger: logger}

	// The AppHost enables the admin endpoints (behind the dashboard commands) only in run mode.
	var admin *adminAPI
	if strings.EqualFold(os.Getenv("FLAGS_ADMIN_ENABLED"), "true") {
		admin = newAdminAPI(api, port)
	}

	router := gin.New()
	router.HandleMethodNotAllowed = true
	configureTrustedProxies(router, logger)
	setupMiddleware(router)
	setupRoutes(router, api, admin)

	server := &http.Server{Addr: fmt.Sprintf(":%d", port), Handler: router, ReadHeaderTimeout: 10 * time.Second}
	serverErr := make(chan error, 1)
	go func() { serverErr <- server.ListenAndServe() }()
	logger.Info("Feature flag service listening", "port", port, "flags", len(seedFlags), "admin_endpoints", admin != nil)

	select {
	case err := <-serverErr:
		if !errors.Is(err, http.ErrServerClosed) {
			logger.Error("Server failed", "error", err)
		}
	case <-ctx.Done():
		logger.Info("Shutting down; draining in-flight requests")
	}

	shutdownCtx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
	defer cancel()
	if err := server.Shutdown(shutdownCtx); err != nil {
		logger.Error("Graceful shutdown failed", "error", err)
	}
	if err := shutdownTelemetry(shutdownCtx); err != nil {
		fmt.Fprintf(os.Stderr, "Failed to flush telemetry: %v\n", err)
	}
}
