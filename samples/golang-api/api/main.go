package main

import (
	"context"
	_ "embed"
	"errors"
	"fmt"
	"log"
	"log/slog"
	"net"
	"net/http"
	"os"
	"os/signal"
	"runtime/debug"
	"strconv"
	"strings"
	"syscall"
	"time"

	"github.com/go-chi/chi/v5"
	"go.opentelemetry.io/contrib/instrumentation/net/http/otelhttp"
	"go.opentelemetry.io/otel/attribute"
	"go.opentelemetry.io/otel/codes"
	"go.opentelemetry.io/otel/trace"
)

// openAPISpec is the OpenAPI 3.1 document describing this API. It is served
// verbatim from /openapi.json and powers the Scalar API reference page.
//
//go:embed openapi.json
var openAPISpec []byte

// apiReferenceHTML is the themed Scalar API reference page served from /scalar
// and, for browsers, from /.
//
//go:embed reference.html
var apiReferenceHTML []byte

const (
	maxWorkOrders   = 1000
	defaultBindHost = "127.0.0.1"
)

// ServiceInfo is returned from / to non-browser clients.
type ServiceInfo struct {
	Name         string `json:"name"`
	Version      string `json:"version"`
	APIReference string `json:"apiReference"`
	OpenAPI      string `json:"openApi"`
}

// writeAPIReference serves the themed Scalar API reference page.
func writeAPIReference(w http.ResponseWriter) {
	w.Header().Set("Content-Type", "text/html; charset=utf-8")
	if _, err := w.Write(apiReferenceHTML); err != nil {
		slog.Error("Failed to write API reference", "error", err)
	}
}

// prefersHTML reports whether the client's Accept header indicates a preference
// for an HTML response, i.e. a web browser. Programmatic clients (curl, fetch,
// SDKs) typically send */* or application/json and keep receiving JSON, so the
// existing API contract is preserved. Only an explicit text/html with a
// non-zero quality value opts into the interactive reference.
func prefersHTML(accept string) bool {
	for _, part := range strings.Split(accept, ",") {
		mediaType := strings.TrimSpace(part)
		if mediaType == "" {
			continue
		}

		quality := 1.0
		if idx := strings.IndexByte(mediaType, ';'); idx >= 0 {
			params := mediaType[idx+1:]
			mediaType = strings.TrimSpace(mediaType[:idx])
			for _, param := range strings.Split(params, ";") {
				param = strings.TrimSpace(param)
				if value, ok := strings.CutPrefix(param, "q="); ok {
					if q, err := strconv.ParseFloat(strings.TrimSpace(value), 64); err == nil {
						quality = q
					}
				}
			}
		}

		if strings.EqualFold(mediaType, "text/html") && quality > 0 {
			return true
		}
	}
	return false
}

// routeTelemetry runs inside the otelhttp server span. Once chi has matched a route, it renames the span to
// "METHOD /route" and adds http.route to the span and to the http.server.* metrics.
func routeTelemetry(next http.Handler) http.Handler {
	return http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		next.ServeHTTP(w, r)
		route := chi.RouteContext(r.Context()).RoutePattern()
		if route == "" {
			return
		}
		span := trace.SpanFromContext(r.Context())
		span.SetName(r.Method + " " + route)
		span.SetAttributes(attribute.String("http.route", route))
		if labeler, ok := otelhttp.LabelerFromContext(r.Context()); ok {
			labeler.Add(attribute.String("http.route", route))
		}
	})
}

// recoverer turns panics into a logged error and a 500 problem details response.
func recoverer(logger *slog.Logger) func(http.Handler) http.Handler {
	return func(next http.Handler) http.Handler {
		return http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
			defer func() {
				if v := recover(); v != nil {
					if v == http.ErrAbortHandler {
						panic(v)
					}
					trace.SpanFromContext(r.Context()).SetStatus(codes.Error, fmt.Sprint(v))
					logger.ErrorContext(r.Context(), "Unhandled panic", "panic", fmt.Sprint(v), "stack", string(debug.Stack()))
					writeInternalError(w, r)
				}
			}()
			next.ServeHTTP(w, r)
		})
	}
}

func newRouter(h *handlers, logger *slog.Logger, adminEnabled bool) http.Handler {
	r := chi.NewRouter()
	r.Use(routeTelemetry, recoverer(logger))

	r.NotFound(func(w http.ResponseWriter, r *http.Request) {
		writeProblem(w, r, ProblemDetails{Status: http.StatusNotFound, Title: "Resource not found",
			Detail: "No route matches this path. See /scalar for the API reference."})
	})
	r.MethodNotAllowed(func(w http.ResponseWriter, r *http.Request) {
		writeProblem(w, r, ProblemDetails{Status: http.StatusMethodNotAllowed, Title: "Method not allowed",
			Detail: r.Method + " isn't supported on this path. See /scalar for the API reference."})
	})

	r.Get("/", func(w http.ResponseWriter, r *http.Request) {
		// Browsers land on the interactive Scalar API reference; programmatic
		// clients keep receiving JSON service metadata.
		if prefersHTML(r.Header.Get("Accept")) {
			writeAPIReference(w)
			return
		}
		writeJSON(w, http.StatusOK, ServiceInfo{Name: "Field Service Work Orders API", Version: "1.0.0", APIReference: "/scalar", OpenAPI: "/openapi.json"})
	})
	r.Get("/scalar", func(w http.ResponseWriter, r *http.Request) { writeAPIReference(w) })
	r.Get("/reference", func(w http.ResponseWriter, r *http.Request) {
		http.Redirect(w, r, "/scalar", http.StatusMovedPermanently)
	})
	r.Get("/openapi.json", func(w http.ResponseWriter, r *http.Request) {
		w.Header().Set("Content-Type", "application/json; charset=utf-8")
		if _, err := w.Write(openAPISpec); err != nil {
			slog.Error("Failed to write OpenAPI document", "error", err)
		}
	})
	r.Get("/health", func(w http.ResponseWriter, r *http.Request) {
		writeJSON(w, http.StatusOK, map[string]string{"status": "healthy"})
	})

	r.Get("/work-orders", h.listWorkOrders)
	r.Post("/work-orders", h.createWorkOrder)
	r.Get("/work-orders/{id}", h.getWorkOrder)
	r.Patch("/work-orders/{id}", h.updateWorkOrder)
	r.Delete("/work-orders/{id}", h.deleteWorkOrder)

	// Seed and simulate exist only for local runs; the AppHost enables them in run mode.
	if adminEnabled {
		r.Post("/admin/seed", h.seed)
		r.Post("/admin/simulate", h.simulate)
	}
	return r
}

func main() {
	os.Exit(run())
}

// run starts the API and returns the process exit code once it stops, after flushing telemetry.
func run() int {
	ctx, stop := signal.NotifyContext(context.Background(), os.Interrupt, syscall.SIGTERM)
	defer stop()

	logger, shutdownTelemetry, err := setupTelemetry(ctx)
	if err != nil {
		log.Printf("set up OpenTelemetry: %v", err)
		return 1
	}
	defer func() {
		flushCtx, cancel := context.WithTimeout(context.Background(), 5*time.Second)
		defer cancel()
		if err := shutdownTelemetry(flushCtx); err != nil {
			log.Printf("shut down OpenTelemetry: %v", err)
		}
	}()

	svc, err := NewService(NewStore(maxWorkOrders), logger)
	if err != nil {
		logger.Error("Failed to create work-order instruments", "error", err.Error())
		return 1
	}
	svc.Seed(ctx)

	port := os.Getenv("PORT")
	if port == "" {
		port = "8080"
	}
	host := os.Getenv("HOST")
	if host == "" {
		host = defaultBindHost
	}
	selfHost := host
	if ip := net.ParseIP(host); ip != nil && ip.IsUnspecified() {
		selfHost = "127.0.0.1"
	}

	adminEnabled, _ := strconv.ParseBool(os.Getenv("WORKORDERS_ADMIN_ENABLED"))
	h := &handlers{svc: svc, simulator: NewSimulator("http://"+net.JoinHostPort(selfHost, port), svc)}
	handler := otelhttp.NewHandler(newRouter(h, logger, adminEnabled), "api",
		otelhttp.WithFilter(func(r *http.Request) bool { return r.URL.Path != "/health" }),
		otelhttp.WithSpanNameFormatter(func(_ string, r *http.Request) string { return r.Method }))

	addr := net.JoinHostPort(host, port)
	server := &http.Server{
		Addr:              addr,
		Handler:           handler,
		ReadTimeout:       5 * time.Second,
		ReadHeaderTimeout: 2 * time.Second,
		WriteTimeout:      15 * time.Second,
		IdleTimeout:       60 * time.Second,
	}

	serverErrs := make(chan error, 1)
	go func() {
		logger.Info("Starting work-order API", "address", addr, "admin_enabled", adminEnabled)
		if err := server.ListenAndServe(); err != nil && !errors.Is(err, http.ErrServerClosed) {
			serverErrs <- err
		}
		close(serverErrs)
	}()

	select {
	case err, ok := <-serverErrs:
		if ok && err != nil {
			logger.Error("Server error", "error", err.Error())
			return 1
		}
	case <-ctx.Done():
		logger.Info("Shutdown signal received, draining in-flight requests")
	}

	shutdownCtx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
	defer cancel()
	if err := server.Shutdown(shutdownCtx); err != nil {
		logger.Error("Server shutdown failed", "error", err.Error())
		return 1
	}
	return 0
}
