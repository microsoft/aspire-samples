package main

import (
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"net/http"
	"reflect"
	"strings"

	"github.com/gin-gonic/gin"
	"go.opentelemetry.io/otel/trace"
)

const maxBodyBytes = 64 << 10

// Problem is an RFC 9457 problem details response.
type Problem struct {
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
	http.StatusUnsupportedMediaType:  "https://tools.ietf.org/html/rfc9110#section-15.5.16",
	http.StatusInternalServerError:   "https://tools.ietf.org/html/rfc9110#section-15.6.1",
}

func writeProblem(c *gin.Context, status int, detail string, errs map[string][]string) {
	title := http.StatusText(status)
	if len(errs) > 0 {
		title = "One or more validation errors occurred."
	}
	problem := Problem{
		Type:     problemTypes[status],
		Title:    title,
		Status:   status,
		Detail:   detail,
		Instance: c.Request.URL.Path,
		Errors:   errs,
	}
	if sc := trace.SpanContextFromContext(c.Request.Context()); sc.HasTraceID() {
		problem.TraceID = sc.TraceID().String()
	}
	c.Header("Content-Type", "application/problem+json")
	c.AbortWithStatusJSON(status, problem)
}

// bindJSON strictly decodes a JSON body (unknown fields rejected) and writes a problem response on failure.
func bindJSON(c *gin.Context, target any) bool {
	if mediaType := c.ContentType(); mediaType != "application/json" && !strings.HasSuffix(mediaType, "+json") {
		writeProblem(c, http.StatusUnsupportedMediaType, "Send the request body as application/json.", nil)
		return false
	}
	decoder := json.NewDecoder(http.MaxBytesReader(c.Writer, c.Request.Body, maxBodyBytes))
	decoder.DisallowUnknownFields()
	if err := decoder.Decode(target); err != nil {
		var tooLarge *http.MaxBytesError
		var typeErr *json.UnmarshalTypeError
		switch {
		case errors.As(err, &tooLarge):
			writeProblem(c, http.StatusRequestEntityTooLarge, fmt.Sprintf("The request body must be at most %d KB.", maxBodyBytes>>10), nil)
		case errors.As(err, &typeErr):
			writeProblem(c, http.StatusBadRequest, "The request body has a field of the wrong type.",
				map[string][]string{typeErr.Field: {"Expected " + jsonTypeName(typeErr.Type.Kind()) + "."}})
		case strings.HasPrefix(err.Error(), "json: unknown field "):
			field := strings.Trim(strings.TrimPrefix(err.Error(), "json: unknown field "), `"`)
			writeProblem(c, http.StatusBadRequest, "The request body has a field that isn't part of the schema.",
				map[string][]string{field: {"Unknown field. See /openapi.json for the request schema."}})
		case errors.Is(err, io.EOF):
			writeProblem(c, http.StatusBadRequest, "The request body is empty.", nil)
		default:
			writeProblem(c, http.StatusBadRequest, "The request body isn't valid JSON: "+strings.TrimPrefix(err.Error(), "json: "), nil)
		}
		return false
	}
	return true
}

func jsonTypeName(kind reflect.Kind) string {
	switch kind {
	case reflect.Bool:
		return "a boolean"
	case reflect.String:
		return "a string"
	case reflect.Int, reflect.Int64, reflect.Float64:
		return "a number"
	case reflect.Slice:
		return "an array"
	default:
		return "an object"
	}
}
