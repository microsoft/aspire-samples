package com.microsoft.aspire.samples.petclinic;

import java.net.URI;
import java.util.Map;
import java.util.TreeMap;

import io.opentelemetry.api.trace.Span;
import io.opentelemetry.api.trace.StatusCode;

import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.springframework.http.HttpHeaders;
import org.springframework.http.HttpStatus;
import org.springframework.http.HttpStatusCode;
import org.springframework.http.ProblemDetail;
import org.springframework.http.ResponseEntity;
import org.springframework.web.bind.MethodArgumentNotValidException;
import org.springframework.web.bind.annotation.ExceptionHandler;
import org.springframework.web.bind.annotation.RestControllerAdvice;
import org.springframework.web.context.request.ServletWebRequest;
import org.springframework.web.context.request.WebRequest;
import org.springframework.web.method.annotation.HandlerMethodValidationException;
import org.springframework.web.servlet.mvc.method.annotation.ResponseEntityExceptionHandler;

/**
 * Renders every error as RFC 9457 problem details ({@code application/problem+json}). Validation problems list the
 * invalid fields under {@code errors}, and every problem carries the request's {@code traceId} so it can be found in
 * the Aspire dashboard.
 */
@RestControllerAdvice
public class ProblemDetailsAdvice extends ResponseEntityExceptionHandler {
    private static final Logger log = LoggerFactory.getLogger(ProblemDetailsAdvice.class);

    @Override
    protected ResponseEntity<Object> handleMethodArgumentNotValid(MethodArgumentNotValidException ex,
        HttpHeaders headers, HttpStatusCode status, WebRequest request) {
        var errors = new TreeMap<String, String>();
        ex.getBindingResult().getFieldErrors().forEach(error -> errors.putIfAbsent(error.getField(), error.getDefaultMessage()));
        return handleExceptionInternal(ex, validationProblem(ex.getBody(), errors), headers, status, request);
    }

    @Override
    protected ResponseEntity<Object> handleHandlerMethodValidationException(HandlerMethodValidationException ex,
        HttpHeaders headers, HttpStatusCode status, WebRequest request) {
        var errors = new TreeMap<String, String>();
        ex.getParameterValidationResults().forEach(result -> result.getResolvableErrors()
            .forEach(error -> errors.putIfAbsent(result.getMethodParameter().getParameterName(), error.getDefaultMessage())));
        return handleExceptionInternal(ex, validationProblem(ex.getBody(), errors), headers, status, request);
    }

    @ExceptionHandler(Exception.class)
    ResponseEntity<Object> handleUnexpected(Exception ex, WebRequest request) {
        log.error("Request failed", ex);
        var span = Span.current();
        span.recordException(ex);
        span.setStatus(StatusCode.ERROR, ex.getMessage());
        var problem = ProblemDetail.forStatusAndDetail(HttpStatus.INTERNAL_SERVER_ERROR,
            "The clinic couldn't complete the request. Look up the trace ID in the Aspire dashboard for details.");
        problem.setTitle("Unexpected error");
        return handleExceptionInternal(ex, problem, new HttpHeaders(), HttpStatus.INTERNAL_SERVER_ERROR, request);
    }

    @Override
    protected ResponseEntity<Object> handleExceptionInternal(Exception ex, Object body, HttpHeaders headers,
        HttpStatusCode statusCode, WebRequest request) {
        var response = super.handleExceptionInternal(ex, body, headers, statusCode, request);
        if (response != null && response.getBody() instanceof ProblemDetail problem) {
            if (problem.getInstance() == null && request instanceof ServletWebRequest servletRequest) {
                problem.setInstance(URI.create(servletRequest.getRequest().getRequestURI()));
            }
            var spanContext = Span.current().getSpanContext();
            if (spanContext.isValid()) {
                problem.setProperty("traceId", spanContext.getTraceId());
            }
        }
        return response;
    }

    private static ProblemDetail validationProblem(ProblemDetail problem, Map<String, String> errors) {
        problem.setType(URI.create(ClinicProblem.TYPE_PREFIX + "validation"));
        problem.setTitle("Validation failed");
        problem.setDetail("One or more fields are invalid.");
        problem.setProperty("errors", errors);
        return problem;
    }
}
