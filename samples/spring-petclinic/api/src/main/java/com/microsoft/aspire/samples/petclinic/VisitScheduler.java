package com.microsoft.aspire.samples.petclinic;

import static com.microsoft.aspire.samples.petclinic.ClinicProblem.SLOT_FORMAT;
import static com.microsoft.aspire.samples.petclinic.ClinicTelemetry.BOOKING_OUTCOME;
import static com.microsoft.aspire.samples.petclinic.ClinicTelemetry.NEXT_AVAILABLE_SLOT;
import static com.microsoft.aspire.samples.petclinic.ClinicTelemetry.OWNER_ID;
import static com.microsoft.aspire.samples.petclinic.ClinicTelemetry.PET_ID;
import static com.microsoft.aspire.samples.petclinic.ClinicTelemetry.PET_NAME;
import static com.microsoft.aspire.samples.petclinic.ClinicTelemetry.PET_SPECIES;
import static com.microsoft.aspire.samples.petclinic.ClinicTelemetry.VETS_CONSIDERED;
import static com.microsoft.aspire.samples.petclinic.ClinicTelemetry.VET_ID;
import static com.microsoft.aspire.samples.petclinic.ClinicTelemetry.VET_NAME;
import static com.microsoft.aspire.samples.petclinic.ClinicTelemetry.VET_SPECIALTY;
import static com.microsoft.aspire.samples.petclinic.ClinicTelemetry.VISIT_ID;
import static com.microsoft.aspire.samples.petclinic.ClinicTelemetry.VISIT_REASON;
import static com.microsoft.aspire.samples.petclinic.ClinicTelemetry.VISIT_STARTS_AT;
import static com.microsoft.aspire.samples.petclinic.ClinicTelemetry.VISIT_STATUS;
import static java.util.stream.Collectors.groupingBy;
import static java.util.stream.Collectors.mapping;
import static java.util.stream.Collectors.toSet;

import java.time.Clock;
import java.time.LocalDate;
import java.time.LocalDateTime;
import java.time.LocalTime;
import java.util.List;
import java.util.Locale;
import java.util.Map;
import java.util.Set;

import io.opentelemetry.api.common.Attributes;

import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.springframework.stereotype.Service;
import org.springframework.transaction.annotation.Transactional;

import com.microsoft.aspire.samples.petclinic.ApiModels.BookVisitRequest;
import com.microsoft.aspire.samples.petclinic.ApiModels.StatsResponse;

/**
 * Books pets into 30-minute visit slots with a vet of the right specialty, and closes visits when they're done.
 */
@Service
public class VisitScheduler {
    static final LocalTime FIRST_SLOT = LocalTime.of(8, 0);
    static final LocalTime LAST_SLOT = LocalTime.of(17, 30);
    static final int SLOT_MINUTES = 30;
    static final int BOOKING_HORIZON_DAYS = 60;
    private static final int NEXT_SLOT_SEARCH_DAYS = 3;
    private static final Logger log = LoggerFactory.getLogger(VisitScheduler.class);

    private final OwnerRepository owners;
    private final PetRepository pets;
    private final VetRepository vets;
    private final VisitRepository visits;
    private final ClinicTelemetry telemetry;
    private final Clock clock;

    public VisitScheduler(OwnerRepository owners, PetRepository pets, VetRepository vets, VisitRepository visits,
        ClinicTelemetry telemetry, Clock clock) {
        this.owners = owners;
        this.pets = pets;
        this.vets = vets;
        this.visits = visits;
        this.telemetry = telemetry;
        this.clock = clock;
    }

    @Transactional
    public Visit book(long ownerId, long petId, BookVisitRequest request) {
        var started = System.nanoTime();
        return telemetry.inSpan("book visit", span -> {
            var startsAt = request.startsAt();
            var reason = request.reason();
            span.setAllAttributes(Attributes.of(
                OWNER_ID, ownerId,
                PET_ID, petId,
                VISIT_REASON, reason.name(),
                VISIT_STARTS_AT, startsAt.toString()));
            var outcome = "error";
            try {
                validateSlot(startsAt);
                var pet = pets.findByIdAndOwnerId(petId, ownerId)
                    .orElseThrow(() -> ClinicProblem.petNotFound(ownerId, petId));
                span.setAttribute(PET_NAME, pet.getName());
                span.setAttribute(PET_SPECIES, pet.getType());
                if (visits.existsByPetIdAndStartsAtAndStatus(petId, startsAt, VisitStatus.SCHEDULED)) {
                    throw ClinicProblem.petAlreadyBooked(pet, startsAt);
                }

                var vet = findAvailableVet(request.vetId(), reason, startsAt);
                var notes = request.notes() == null || request.notes().isBlank() ? null : request.notes().strip();
                var visit = visits.save(new Visit(pet, vet, startsAt, reason, notes));
                span.setAttribute(VISIT_ID, visit.getId());
                span.setAttribute(VET_ID, vet.getId());
                span.setAttribute(VET_SPECIALTY, vet.getSpecialty());
                span.addEvent("visit.booked", Attributes.of(VISIT_ID, visit.getId(), VET_NAME, vet.displayName()));
                telemetry.visitBooked(reason, pet.getType(), vet.getSpecialty());
                log.atInfo()
                    .addKeyValue("petclinic.visit.id", visit.getId())
                    .addKeyValue("petclinic.visit.reason", reason.name())
                    .addKeyValue("petclinic.pet.name", pet.getName())
                    .addKeyValue("petclinic.pet.species", pet.getType())
                    .addKeyValue("petclinic.vet.name", vet.displayName())
                    .addKeyValue("petclinic.visit.starts_at", startsAt.toString())
                    .log("Booked {} for {} the {} with {} on {}",
                        reason.label().toLowerCase(Locale.ROOT), pet.getName(), pet.getType(), vet.displayName(),
                        SLOT_FORMAT.format(startsAt));
                outcome = "booked";
                return visit;
            } catch (ClinicProblem problem) {
                outcome = problem.code();
                throw problem;
            } finally {
                span.setAttribute(BOOKING_OUTCOME, outcome);
                // Recorded while the span is current, so the histogram's exemplars link back to this trace.
                telemetry.bookingFinished((System.nanoTime() - started) / 1e9, outcome);
            }
        });
    }

    @Transactional
    public Visit complete(long visitId) {
        return close(visitId, VisitStatus.COMPLETED, "complete visit");
    }

    @Transactional
    public Visit cancel(long visitId) {
        return close(visitId, VisitStatus.CANCELLED, "cancel visit");
    }

    @Transactional(readOnly = true)
    public List<Visit> schedule(LocalDate from, int days) {
        return visits.findByStartsAtBetweenOrderByStartsAtAscIdAsc(
            from.atStartOfDay(), from.plusDays(days).atStartOfDay().minusSeconds(1));
    }

    @Transactional(readOnly = true)
    public StatsResponse stats() {
        var now = LocalDateTime.now(clock);
        var today = now.toLocalDate();
        return new StatsResponse(owners.count(), pets.count(), vets.count(),
            visits.countByStatusNotAndStartsAtBetween(
                VisitStatus.CANCELLED, today.atStartOfDay(), today.plusDays(1).atStartOfDay().minusSeconds(1)),
            visits.countByStatusAndStartsAtGreaterThanEqual(VisitStatus.SCHEDULED, now));
    }

    /** Publishes visits already on the books (for example after an API restart) to the scheduled-visits counter. */
    @Transactional(readOnly = true)
    public void reportScheduledVisits() {
        visits.countBySpecialty(VisitStatus.SCHEDULED)
            .forEach(count -> telemetry.scheduledVisitsLoaded(count.getSpecialty(), count.getVisits()));
    }

    /** Deletes every scheduled visit from now on. Returns how many were removed. */
    @Transactional
    public int clearUpcoming() {
        var upcoming = visits.findByStatusAndStartsAtGreaterThanEqual(VisitStatus.SCHEDULED, now());
        upcoming.forEach(visit -> telemetry.visitClosed(visit.getVet().getSpecialty()));
        visits.deleteAllInBatch(upcoming);
        return upcoming.size();
    }

    /** Scheduled visits whose start time has passed, earliest first. */
    @Transactional(readOnly = true)
    public List<Visit> pastDue(int limit) {
        var now = now();
        return visits.findByStatusAndStartsAtBetweenOrderByStartsAtAsc(VisitStatus.SCHEDULED, now.minusDays(1), now)
            .stream().limit(limit).toList();
    }

    LocalDateTime now() {
        return LocalDateTime.now(clock);
    }

    private Visit close(long visitId, VisitStatus status, String spanName) {
        return telemetry.inSpan(spanName, span -> {
            span.setAttribute(VISIT_ID, visitId);
            var visit = visits.findWithDetailsById(visitId).orElseThrow(() -> ClinicProblem.visitNotFound(visitId));
            if (visit.getStatus() != VisitStatus.SCHEDULED) {
                throw ClinicProblem.visitNotScheduled(visit);
            }

            visit.setStatus(status);
            var specialty = visit.getVet().getSpecialty();
            span.setAttribute(VISIT_STATUS, status.name());
            span.setAttribute(VET_SPECIALTY, specialty);
            telemetry.visitClosed(specialty);
            log.atInfo()
                .addKeyValue("petclinic.visit.id", visitId)
                .addKeyValue("petclinic.visit.status", status.name())
                .log("Visit {} for {} with {} is {}", visitId, visit.getPet().getName(), visit.getVet().displayName(),
                    status.name().toLowerCase(Locale.ROOT));
            return visit;
        });
    }

    private void validateSlot(LocalDateTime startsAt) {
        if (startsAt.getMinute() % SLOT_MINUTES != 0 || startsAt.getSecond() != 0 || startsAt.getNano() != 0) {
            throw ClinicProblem.invalidVisitTime(
                "Visits start on the hour or half hour, but %s doesn't.".formatted(startsAt.toLocalTime()));
        }
        var time = startsAt.toLocalTime();
        if (time.isBefore(FIRST_SLOT) || time.isAfter(LAST_SLOT)) {
            throw ClinicProblem.invalidVisitTime(
                "The clinic books visits from %s to %s, so %s is outside opening hours.".formatted(FIRST_SLOT, LAST_SLOT, time));
        }
        var now = now();
        if (!startsAt.isAfter(now)) {
            throw ClinicProblem.invalidVisitTime("%s is in the past.".formatted(SLOT_FORMAT.format(startsAt)));
        }
        if (startsAt.isAfter(now.plusDays(BOOKING_HORIZON_DAYS))) {
            throw ClinicProblem.invalidVisitTime(
                "Visits can be booked up to %d days ahead.".formatted(BOOKING_HORIZON_DAYS));
        }
    }

    private Vet findAvailableVet(Long vetId, VisitReason reason, LocalDateTime startsAt) {
        return telemetry.inSpan("find available vet", span -> {
            var candidates = vetId != null
                ? List.of(vets.findById(vetId).orElseThrow(() -> ClinicProblem.vetNotFound(vetId)))
                : vets.findBySpecialtyOrderByLastNameAscFirstNameAsc(reason.specialty());
            span.setAttribute(VET_SPECIALTY, vetId != null ? candidates.getFirst().getSpecialty() : reason.specialty());
            span.setAttribute(VETS_CONSIDERED, candidates.size());

            var searchUntil = startsAt.plusDays(NEXT_SLOT_SEARCH_DAYS);
            Map<Long, Set<LocalDateTime>> booked = visits.findByVetIdInAndStatusAndStartsAtBetween(
                    candidates.stream().map(Vet::getId).toList(), VisitStatus.SCHEDULED, startsAt, searchUntil)
                .stream()
                .collect(groupingBy(visit -> visit.getVet().getId(), mapping(Visit::getStartsAt, toSet())));

            for (var vet : candidates) {
                var vetAttributes = Attributes.of(VET_ID, vet.getId(), VET_NAME, vet.displayName());
                if (!booked.getOrDefault(vet.getId(), Set.of()).contains(startsAt)) {
                    span.addEvent("vet.assigned", vetAttributes);
                    return vet;
                }
                span.addEvent("vet.busy", vetAttributes);
            }

            var next = nextFreeSlot(candidates, booked, startsAt, searchUntil);
            if (next != null) {
                span.setAttribute(NEXT_AVAILABLE_SLOT, next.toString());
            }
            var who = vetId != null
                ? candidates.getFirst().displayName() + " isn't free"
                : "No " + reason.specialty().toLowerCase(Locale.ROOT) + " vet is free";
            log.atWarn()
                .addKeyValue("petclinic.visit.reason", reason.name())
                .addKeyValue("petclinic.vet.specialty", reason.specialty())
                .addKeyValue("petclinic.visit.starts_at", startsAt.toString())
                .addKeyValue("petclinic.visit.next_available_slot", next == null ? "none" : next.toString())
                .log("{} on {}; next free slot {}", who, SLOT_FORMAT.format(startsAt),
                    next == null ? "none in the next three days" : SLOT_FORMAT.format(next));
            throw ClinicProblem.slotUnavailable("%s on %s.".formatted(who, SLOT_FORMAT.format(startsAt)), next);
        });
    }

    private static LocalDateTime nextFreeSlot(List<Vet> candidates, Map<Long, Set<LocalDateTime>> booked,
        LocalDateTime after, LocalDateTime until) {
        for (var slot = nextSlot(after); slot.isBefore(until); slot = nextSlot(slot)) {
            for (var vet : candidates) {
                if (!booked.getOrDefault(vet.getId(), Set.of()).contains(slot)) {
                    return slot;
                }
            }
        }
        return null;
    }

    static LocalDateTime nextSlot(LocalDateTime slot) {
        var next = slot.plusMinutes(SLOT_MINUTES);
        return next.toLocalDate().equals(slot.toLocalDate()) && !next.toLocalTime().isAfter(LAST_SLOT)
            ? next
            : slot.toLocalDate().plusDays(1).atTime(FIRST_SLOT);
    }
}
