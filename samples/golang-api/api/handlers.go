package main

import (
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"log/slog"
	"net/http"
	"strconv"
	"strings"
	"time"

	"github.com/go-chi/chi/v5"
	"go.opentelemetry.io/otel/trace"
)

const (
	maxJSONRequestBodyBytes = 1 << 20
	maxSimulationSteps      = 50
)

// ProblemDetails is an RFC 9457 (formerly RFC 7807) error response. Errors lists invalid fields for
// validation problems, and TraceID links the response to its trace in the Aspire dashboard.
type ProblemDetails struct {
	Type     string              `json:"type"`
	Title    string              `json:"title"`
	Status   int                 `json:"status"`
	Detail   string              `json:"detail,omitempty"`
	Instance string              `json:"instance,omitempty"`
	Errors   map[string][]string `json:"errors,omitempty"`
	TraceID  string              `json:"traceId,omitempty"`
}

var problemTypes = map[int]string{
	http.StatusBadRequest:            "https://tools.ietf.org/html/rfc9110#section-15.5.1",
	http.StatusNotFound:              "https://tools.ietf.org/html/rfc9110#section-15.5.5",
	http.StatusMethodNotAllowed:      "https://tools.ietf.org/html/rfc9110#section-15.5.6",
	http.StatusConflict:              "https://tools.ietf.org/html/rfc9110#section-15.5.10",
	http.StatusRequestEntityTooLarge: "https://tools.ietf.org/html/rfc9110#section-15.5.14",
	http.StatusInternalServerError:   "https://tools.ietf.org/html/rfc9110#section-15.6.1",
}

func writeProblem(w http.ResponseWriter, r *http.Request, p ProblemDetails) {
	p.Type = problemTypes[p.Status]
	p.Instance = r.URL.Path
	if sc := trace.SpanContextFromContext(r.Context()); sc.HasTraceID() {
		p.TraceID = sc.TraceID().String()
	}
	w.Header().Set("Content-Type", "application/problem+json")
	writeJSON(w, p.Status, p)
}

func writeValidationProblem(w http.ResponseWriter, r *http.Request, errs map[string][]string) {
	writeProblem(w, r, ProblemDetails{
		Status: http.StatusBadRequest,
		Title:  "One or more validation errors occurred.",
		Detail: "Fix the fields listed in errors and try again.",
		Errors: errs,
	})
}

func writeServiceError(w http.ResponseWriter, r *http.Request, err error) {
	var invalid *ValidationError
	switch {
	case errors.As(err, &invalid):
		writeValidationProblem(w, r, invalid.Errors)
	case errors.Is(err, errNotFound):
		writeProblem(w, r, ProblemDetails{Status: http.StatusNotFound, Title: "Work order not found",
			Detail: fmt.Sprintf("No work order exists with ID %s.", chi.URLParam(r, "id"))})
	case errors.Is(err, errClosed):
		writeProblem(w, r, ProblemDetails{Status: http.StatusConflict, Title: "Work order is closed",
			Detail: "Completed and cancelled work orders can't be changed. Open a follow-up work order instead."})
	case errors.Is(err, errStoreFull):
		writeProblem(w, r, ProblemDetails{Status: http.StatusConflict, Title: "Backlog is at capacity",
			Detail: fmt.Sprintf("The in-memory store holds at most %d work orders. Delete closed work orders and try again.", maxWorkOrders)})
	default:
		writeInternalError(w, r)
	}
}

func writeInternalError(w http.ResponseWriter, r *http.Request) {
	writeProblem(w, r, ProblemDetails{Status: http.StatusInternalServerError, Title: "An unexpected error occurred.",
		Detail: "Use the traceId to find the failed request in the Aspire dashboard."})
}

func writeJSON(w http.ResponseWriter, status int, v any) {
	if w.Header().Get("Content-Type") == "" {
		w.Header().Set("Content-Type", "application/json")
	}
	w.WriteHeader(status)
	if err := json.NewEncoder(w).Encode(v); err != nil {
		slog.Error("Failed to write JSON response", "error", err)
	}
}

// decodeJSONBody reads a single JSON object, rejecting unknown fields, trailing data, and bodies over 1 MiB.
func decodeJSONBody(w http.ResponseWriter, r *http.Request, dst any) bool {
	r.Body = http.MaxBytesReader(w, r.Body, maxJSONRequestBodyBytes)
	defer r.Body.Close()

	decoder := json.NewDecoder(r.Body)
	decoder.DisallowUnknownFields()
	err := decoder.Decode(dst)
	if err == nil {
		if decoder.Decode(&struct{}{}) != io.EOF {
			err = errors.New("the body must contain a single JSON object")
		}
	}
	if err == nil {
		return true
	}

	var tooLarge *http.MaxBytesError
	var typeErr *json.UnmarshalTypeError
	var timeErr *time.ParseError
	switch {
	case errors.As(err, &tooLarge):
		writeProblem(w, r, ProblemDetails{Status: http.StatusRequestEntityTooLarge, Title: "Request body too large",
			Detail: "The request body must be 1 MiB or smaller."})
	case errors.As(err, &typeErr) && typeErr.Field != "":
		writeValidationProblem(w, r, map[string][]string{typeErr.Field: {fmt.Sprintf("The value must be a %s.", typeErr.Type)}})
	case errors.As(err, &timeErr):
		// reportedAt is the only timestamp that clients send.
		writeValidationProblem(w, r, map[string][]string{"reportedAt": {"The reported time must be an RFC 3339 timestamp such as 2026-11-26T18:30:00Z."}})
	case strings.HasPrefix(err.Error(), "json: unknown field "):
		field := strings.Trim(strings.TrimPrefix(err.Error(), "json: unknown field "), `"`)
		writeValidationProblem(w, r, map[string][]string{field: {"This field isn't part of the work-order schema."}})
	default:
		writeProblem(w, r, ProblemDetails{Status: http.StatusBadRequest, Title: "The request body isn't valid JSON.", Detail: err.Error()})
	}
	return false
}

func parseID(w http.ResponseWriter, r *http.Request) (int, bool) {
	id, err := strconv.Atoi(chi.URLParam(r, "id"))
	if err != nil || id < 1 {
		writeValidationProblem(w, r, map[string][]string{"id": {"The work order ID must be a positive integer."}})
		return 0, false
	}
	return id, true
}

type handlers struct {
	svc       *Service
	simulator *Simulator
}

func (h *handlers) listWorkOrders(w http.ResponseWriter, r *http.Request) {
	q := r.URL.Query()
	orders, err := h.svc.List(r.Context(), Filter{Status: Status(q.Get("status")), Priority: Priority(q.Get("priority")), Assignee: q.Get("assignee")})
	if err != nil {
		writeServiceError(w, r, err)
		return
	}
	writeJSON(w, http.StatusOK, orders)
}

func (h *handlers) getWorkOrder(w http.ResponseWriter, r *http.Request) {
	id, ok := parseID(w, r)
	if !ok {
		return
	}
	o, err := h.svc.Get(r.Context(), id)
	if err != nil {
		writeServiceError(w, r, err)
		return
	}
	writeJSON(w, http.StatusOK, o)
}

func (h *handlers) createWorkOrder(w http.ResponseWriter, r *http.Request) {
	var req CreateWorkOrderRequest
	if !decodeJSONBody(w, r, &req) {
		return
	}
	o, err := h.svc.Create(r.Context(), req)
	if err != nil {
		writeServiceError(w, r, err)
		return
	}
	w.Header().Set("Location", fmt.Sprintf("/work-orders/%d", o.ID))
	writeJSON(w, http.StatusCreated, o)
}

func (h *handlers) updateWorkOrder(w http.ResponseWriter, r *http.Request) {
	id, ok := parseID(w, r)
	if !ok {
		return
	}
	var req UpdateWorkOrderRequest
	if !decodeJSONBody(w, r, &req) {
		return
	}
	o, err := h.svc.Update(r.Context(), id, req)
	if err != nil {
		writeServiceError(w, r, err)
		return
	}
	writeJSON(w, http.StatusOK, o)
}

func (h *handlers) deleteWorkOrder(w http.ResponseWriter, r *http.Request) {
	id, ok := parseID(w, r)
	if !ok {
		return
	}
	if err := h.svc.Delete(r.Context(), id); err != nil {
		writeServiceError(w, r, err)
		return
	}
	w.WriteHeader(http.StatusNoContent)
}

func (h *handlers) seed(w http.ResponseWriter, r *http.Request) {
	writeJSON(w, http.StatusOK, map[string]int{"seeded": h.svc.Seed(r.Context())})
}

func (h *handlers) simulate(w http.ResponseWriter, r *http.Request) {
	steps := 12
	if v := r.URL.Query().Get("steps"); v != "" {
		n, err := strconv.Atoi(v)
		if err != nil || n < 1 || n > maxSimulationSteps {
			writeValidationProblem(w, r, map[string][]string{"steps": {fmt.Sprintf("Steps must be a whole number from 1 to %d.", maxSimulationSteps)}})
			return
		}
		steps = n
	}
	result, err := h.simulator.Run(r.Context(), steps)
	if err != nil {
		h.svc.log.ErrorContext(r.Context(), "Traffic simulation failed", "error", err.Error())
		writeInternalError(w, r)
		return
	}
	writeJSON(w, http.StatusOK, result)
}
