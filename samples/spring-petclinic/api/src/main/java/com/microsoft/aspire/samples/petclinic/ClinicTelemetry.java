package com.microsoft.aspire.samples.petclinic;

import java.util.List;
import java.util.function.Function;

import io.opentelemetry.api.GlobalOpenTelemetry;
import io.opentelemetry.api.OpenTelemetry;
import io.opentelemetry.api.common.AttributeKey;
import io.opentelemetry.api.common.Attributes;
import io.opentelemetry.api.metrics.DoubleHistogram;
import io.opentelemetry.api.metrics.LongCounter;
import io.opentelemetry.api.metrics.LongUpDownCounter;
import io.opentelemetry.api.metrics.Meter;
import io.opentelemetry.api.trace.Span;
import io.opentelemetry.api.trace.SpanBuilder;
import io.opentelemetry.api.trace.StatusCode;
import io.opentelemetry.api.trace.Tracer;
import io.opentelemetry.context.Scope;

import org.springframework.stereotype.Component;

/**
 * The clinic's tracer and the {@code Petclinic.Api} meter.
 *
 * <p>The OpenTelemetry Java agent that Aspire attaches installs {@link GlobalOpenTelemetry} before the application
 * starts, so these instruments export through the agent's OTLP exporter. Without the agent (tests) they are no-ops.
 * Metric attributes only use small, fixed sets of values; owner, pet, and visit IDs go on spans and logs.
 */
@Component
public class ClinicTelemetry {
    public static final String INSTRUMENTATION_SCOPE = "Petclinic.Api";

    static final AttributeKey<Long> OWNER_ID = AttributeKey.longKey("petclinic.owner.id");
    static final AttributeKey<String> OWNER_CITY = AttributeKey.stringKey("petclinic.owner.city");
    static final AttributeKey<Long> PET_ID = AttributeKey.longKey("petclinic.pet.id");
    static final AttributeKey<String> PET_NAME = AttributeKey.stringKey("petclinic.pet.name");
    static final AttributeKey<String> PET_SPECIES = AttributeKey.stringKey("petclinic.pet.species");
    static final AttributeKey<Long> VET_ID = AttributeKey.longKey("petclinic.vet.id");
    static final AttributeKey<String> VET_NAME = AttributeKey.stringKey("petclinic.vet.name");
    static final AttributeKey<String> VET_SPECIALTY = AttributeKey.stringKey("petclinic.vet.specialty");
    static final AttributeKey<Long> VETS_CONSIDERED = AttributeKey.longKey("petclinic.vets.considered");
    static final AttributeKey<Long> VISIT_ID = AttributeKey.longKey("petclinic.visit.id");
    static final AttributeKey<String> VISIT_REASON = AttributeKey.stringKey("petclinic.visit.reason");
    static final AttributeKey<String> VISIT_STARTS_AT = AttributeKey.stringKey("petclinic.visit.starts_at");
    static final AttributeKey<String> VISIT_STATUS = AttributeKey.stringKey("petclinic.visit.status");
    static final AttributeKey<String> NEXT_AVAILABLE_SLOT = AttributeKey.stringKey("petclinic.visit.next_available_slot");
    static final AttributeKey<String> BOOKING_OUTCOME = AttributeKey.stringKey("petclinic.booking.outcome");
    static final AttributeKey<String> PROBLEM_TYPE = AttributeKey.stringKey("petclinic.problem.type");
    static final AttributeKey<String> PROBLEM_DETAIL = AttributeKey.stringKey("petclinic.problem.detail");

    private final Tracer tracer;
    private final LongCounter visitsBooked;
    private final DoubleHistogram bookingDuration;
    private final LongUpDownCounter visitsScheduled;
    private final LongCounter ownersRegistered;

    public ClinicTelemetry() {
        this(GlobalOpenTelemetry.get());
    }

    ClinicTelemetry(OpenTelemetry openTelemetry) {
        tracer = openTelemetry.getTracer(INSTRUMENTATION_SCOPE);
        Meter meter = openTelemetry.getMeter(INSTRUMENTATION_SCOPE);
        visitsBooked = meter.counterBuilder("petclinic.visits.booked")
            .setUnit("{visit}")
            .setDescription("Visits booked, by reason and pet species.")
            .build();
        bookingDuration = meter.histogramBuilder("petclinic.visit.booking.duration")
            .setUnit("s")
            .setDescription("Time to validate a visit request, find a free vet, and book the slot, by outcome.")
            .setExplicitBucketBoundariesAdvice(List.of(0.005, 0.01, 0.025, 0.05, 0.1, 0.25, 0.5, 1.0, 2.5))
            .build();
        visitsScheduled = meter.upDownCounterBuilder("petclinic.visits.scheduled")
            .setUnit("{visit}")
            .setDescription("Visits on the books that haven't been completed or cancelled, by vet specialty.")
            .build();
        ownersRegistered = meter.counterBuilder("petclinic.owners.registered")
            .setUnit("{owner}")
            .setDescription("Pet owners registered at the front desk.")
            .build();
    }

    public Tracer tracer() {
        return tracer;
    }

    /**
     * Runs {@code work} in a new span that is current while it runs. A {@link ClinicProblem} is recorded as a
     * {@code request.rejected} event (it's an expected outcome, not an error); anything else marks the span as failed.
     */
    <T> T inSpan(String name, Function<Span, T> work) {
        return inSpan(tracer.spanBuilder(name), work);
    }

    <T> T inSpan(SpanBuilder builder, Function<Span, T> work) {
        Span span = builder.startSpan();
        try (Scope ignored = span.makeCurrent()) {
            return work.apply(span);
        } catch (ClinicProblem problem) {
            span.addEvent("request.rejected", Attributes.of(
                PROBLEM_TYPE, problem.code(),
                PROBLEM_DETAIL, String.valueOf(problem.getBody().getDetail())));
            throw problem;
        } catch (RuntimeException e) {
            span.recordException(e);
            span.setStatus(StatusCode.ERROR, e.getMessage());
            throw e;
        } finally {
            span.end();
        }
    }

    void visitBooked(VisitReason reason, String species, String specialty) {
        visitsBooked.add(1, Attributes.of(VISIT_REASON, reason.name(), PET_SPECIES, species));
        visitsScheduled.add(1, Attributes.of(VET_SPECIALTY, specialty));
    }

    void visitClosed(String specialty) {
        visitsScheduled.add(-1, Attributes.of(VET_SPECIALTY, specialty));
    }

    void scheduledVisitsLoaded(String specialty, long count) {
        visitsScheduled.add(count, Attributes.of(VET_SPECIALTY, specialty));
    }

    void bookingFinished(double seconds, String outcome) {
        bookingDuration.record(seconds, Attributes.of(BOOKING_OUTCOME, outcome));
    }

    void ownerRegistered() {
        ownersRegistered.add(1);
    }
}
