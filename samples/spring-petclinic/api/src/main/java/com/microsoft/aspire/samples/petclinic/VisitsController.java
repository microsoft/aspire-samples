package com.microsoft.aspire.samples.petclinic;

import static com.microsoft.aspire.samples.petclinic.ProblemExamples.PROBLEM_JSON;

import java.net.URI;
import java.time.LocalDate;
import java.util.List;

import io.swagger.v3.oas.annotations.Operation;
import io.swagger.v3.oas.annotations.Parameter;
import io.swagger.v3.oas.annotations.media.Content;
import io.swagger.v3.oas.annotations.media.ExampleObject;
import io.swagger.v3.oas.annotations.media.Schema;
import io.swagger.v3.oas.annotations.responses.ApiResponse;
import io.swagger.v3.oas.annotations.tags.Tag;
import jakarta.validation.Valid;
import jakarta.validation.constraints.Max;
import jakarta.validation.constraints.Min;

import org.springframework.format.annotation.DateTimeFormat;
import org.springframework.http.HttpStatus;
import org.springframework.http.ProblemDetail;
import org.springframework.http.ResponseEntity;
import org.springframework.web.bind.annotation.GetMapping;
import org.springframework.web.bind.annotation.PathVariable;
import org.springframework.web.bind.annotation.PostMapping;
import org.springframework.web.bind.annotation.RequestBody;
import org.springframework.web.bind.annotation.RequestMapping;
import org.springframework.web.bind.annotation.RequestParam;
import org.springframework.web.bind.annotation.ResponseStatus;
import org.springframework.web.bind.annotation.RestController;

import com.microsoft.aspire.samples.petclinic.ApiModels.BookVisitRequest;
import com.microsoft.aspire.samples.petclinic.ApiModels.VisitResponse;

@RestController
@RequestMapping("/api")
@Tag(name = "Visits")
public class VisitsController {
    private final VisitScheduler scheduler;

    public VisitsController(VisitScheduler scheduler) {
        this.scheduler = scheduler;
    }

    @GetMapping("/visits")
    @Operation(operationId = "getSchedule", summary = "Get the visit schedule", description = """
        Returns every visit (scheduled, completed, and cancelled) that starts within `days` days of `from`, in start \
        order, with the pet, its owner, and the vet.""")
    @ApiResponse(responseCode = "200", description = "The visits in the window.")
    @ApiResponse(responseCode = "400", description = "`days` is outside 1-31 or `from` isn't a date.", content = @Content(
        mediaType = PROBLEM_JSON, schema = @Schema(implementation = ProblemDetail.class)))
    public List<VisitResponse> schedule(
        @Parameter(description = "First day of the window. Defaults to today.", example = "2026-12-03")
        @RequestParam(required = false) @DateTimeFormat(iso = DateTimeFormat.ISO.DATE) LocalDate from,
        @Parameter(description = "Number of days in the window.", example = "7")
        @RequestParam(defaultValue = "7") @Min(1) @Max(31) int days) {
        var start = from == null ? scheduler.now().toLocalDate() : from;
        return scheduler.schedule(start, days).stream().map(VisitResponse::from).toList();
    }

    @PostMapping("/owners/{ownerId}/pets/{petId}/visits")
    @ResponseStatus(HttpStatus.CREATED)
    @Operation(operationId = "bookVisit", summary = "Book a visit", description = """
        Books a 30-minute visit for a pet. Visits start on the hour or half hour between 08:00 and 17:30 (clinic time), \
        in the future and at most 60 days ahead.

        Without a `vetId`, the clinic assigns the first free vet whose specialty matches the reason: general practice \
        for wellness exams, vaccinations, and sick visits; dentistry for dental cleanings; radiology for X-rays; and \
        surgery for surgery consults. If nobody is free, the `409` response's `nextAvailableSlot` suggests the next \
        free slot in the next three days.""")
    @io.swagger.v3.oas.annotations.parameters.RequestBody(content = @Content(examples = {
        @ExampleObject(name = "Dental cleaning", summary = "Assign the first free dentist", value = """
            {"startsAt":"2026-12-03T10:30:00","reason":"DENTAL_CLEANING","notes":"Tartar on the upper molars; eats on one side."}"""),
        @ExampleObject(name = "Vaccination with a vet", summary = "Book a specific vet", value = """
            {"startsAt":"2026-12-03T09:00:00","reason":"VACCINATION","vetId":1,"notes":"Rabies booster due."}""") }))
    @ApiResponse(responseCode = "201", description = "The visit was booked.")
    @ApiResponse(responseCode = "400", description = "The start time isn't a bookable slot, or a field is invalid.",
        content = @Content(mediaType = PROBLEM_JSON, schema = @Schema(implementation = ProblemDetail.class),
            examples = @ExampleObject(value = ProblemExamples.INVALID_VISIT_TIME)))
    @ApiResponse(responseCode = "404", description = "The owner has no such pet, or the requested vet doesn't exist.",
        content = @Content(mediaType = PROBLEM_JSON, schema = @Schema(implementation = ProblemDetail.class),
            examples = @ExampleObject(value = ProblemExamples.PET_NOT_FOUND)))
    @ApiResponse(responseCode = "409", description = "No matching vet is free, or the pet already has a visit at that time.",
        content = @Content(mediaType = PROBLEM_JSON, schema = @Schema(implementation = ProblemDetail.class),
            examples = @ExampleObject(value = ProblemExamples.SLOT_UNAVAILABLE)))
    public ResponseEntity<VisitResponse> book(
        @Parameter(description = "Owner ID.", example = "3") @PathVariable long ownerId,
        @Parameter(description = "Pet ID.", example = "3") @PathVariable long petId,
        @Valid @RequestBody BookVisitRequest request) {
        var visit = scheduler.book(ownerId, petId, request);
        return ResponseEntity.created(URI.create("/api/visits?from=" + visit.getStartsAt().toLocalDate() + "&days=1"))
            .body(VisitResponse.from(visit));
    }

    @PostMapping("/visits/{visitId}/complete")
    @Operation(operationId = "completeVisit", summary = "Complete a visit", description = "Checks the pet out after its visit.")
    @ApiResponse(responseCode = "200", description = "The visit is completed.")
    @ApiResponse(responseCode = "404", description = "No visit has this ID.", content = @Content(mediaType = PROBLEM_JSON,
        schema = @Schema(implementation = ProblemDetail.class), examples = @ExampleObject(value = ProblemExamples.VISIT_NOT_FOUND)))
    @ApiResponse(responseCode = "409", description = "The visit was already completed or cancelled.", content = @Content(
        mediaType = PROBLEM_JSON, schema = @Schema(implementation = ProblemDetail.class),
        examples = @ExampleObject(value = ProblemExamples.VISIT_NOT_SCHEDULED)))
    public VisitResponse complete(@Parameter(description = "Visit ID.", example = "42") @PathVariable long visitId) {
        return VisitResponse.from(scheduler.complete(visitId));
    }

    @PostMapping("/visits/{visitId}/cancel")
    @Operation(operationId = "cancelVisit", summary = "Cancel a visit", description = "Cancels a scheduled visit and frees its slot.")
    @ApiResponse(responseCode = "200", description = "The visit is cancelled.")
    @ApiResponse(responseCode = "404", description = "No visit has this ID.", content = @Content(mediaType = PROBLEM_JSON,
        schema = @Schema(implementation = ProblemDetail.class), examples = @ExampleObject(value = ProblemExamples.VISIT_NOT_FOUND)))
    @ApiResponse(responseCode = "409", description = "The visit was already completed or cancelled.", content = @Content(
        mediaType = PROBLEM_JSON, schema = @Schema(implementation = ProblemDetail.class),
        examples = @ExampleObject(value = ProblemExamples.VISIT_NOT_SCHEDULED)))
    public VisitResponse cancel(@Parameter(description = "Visit ID.", example = "42") @PathVariable long visitId) {
        return VisitResponse.from(scheduler.cancel(visitId));
    }
}
