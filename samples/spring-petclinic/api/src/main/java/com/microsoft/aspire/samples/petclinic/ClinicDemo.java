package com.microsoft.aspire.samples.petclinic;

import java.time.LocalDate;
import java.time.LocalDateTime;
import java.time.LocalTime;
import java.util.List;
import java.util.concurrent.ThreadLocalRandom;

import io.opentelemetry.api.trace.Span;
import io.opentelemetry.api.trace.SpanContext;

import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.springframework.data.domain.Sort;
import org.springframework.stereotype.Component;

import com.microsoft.aspire.samples.petclinic.ApiModels.BookVisitRequest;
import com.microsoft.aspire.samples.petclinic.ApiModels.CreateOwnerRequest;
import com.microsoft.aspire.samples.petclinic.ApiModels.CreatePetRequest;

/**
 * Sample schedule and simulated front-desk traffic, so the app and the Aspire dashboard have something to show.
 * Everything goes through the same services as the API, so it produces the same spans, metrics, and logs.
 */
@Component
public class ClinicDemo {
    private static final Logger log = LoggerFactory.getLogger(ClinicDemo.class);

    /** Five days of visits. Pet indexes refer to the sample pets in ID order; times are distinct per day. */
    private static final List<PlannedVisit> SAMPLE_SCHEDULE = List.of(
        new PlannedVisit(0, "08:30", 9, VisitReason.WELLNESS_EXAM, "Senior wellness exam; slowing down on walks."),
        new PlannedVisit(0, "09:00", 0, VisitReason.VACCINATION, "FVRCP booster due."),
        new PlannedVisit(0, "10:00", 2, VisitReason.DENTAL_CLEANING, "Tartar on the upper molars; eats on one side."),
        new PlannedVisit(0, "11:30", 4, VisitReason.IMAGING, "Follow-up X-ray after a fall from the basking shelf."),
        new PlannedVisit(0, "13:00", 6, VisitReason.SICK_VISIT, "Off her food since Tuesday."),
        new PlannedVisit(0, "14:30", 11, VisitReason.SURGERY_CONSULT, "Lump on the left shoulder."),
        new PlannedVisit(0, "16:00", 1, VisitReason.WELLNESS_EXAM, "Overgrown incisors check."),
        new PlannedVisit(1, "08:00", 3, VisitReason.VACCINATION, "Rabies booster due."),
        new PlannedVisit(1, "09:30", 5, VisitReason.IMAGING, "Retained shed near the tail; check the spine."),
        new PlannedVisit(1, "10:30", 7, VisitReason.DENTAL_CLEANING, "Bad breath; possible gingivitis."),
        new PlannedVisit(1, "13:30", 10, VisitReason.SICK_VISIT, "Fluffed up and sleeping more than usual."),
        new PlannedVisit(1, "15:00", 12, VisitReason.WELLNESS_EXAM, "Annual check-up; owner asks about weight."),
        new PlannedVisit(1, "16:30", 9, VisitReason.SURGERY_CONSULT, "Torn dewclaw on the right front paw."),
        new PlannedVisit(2, "09:00", 8, VisitReason.WELLNESS_EXAM, "Beak and nail trim."),
        new PlannedVisit(2, "10:00", 0, VisitReason.IMAGING, "Swallowed a hair tie; check the stomach."),
        new PlannedVisit(2, "11:00", 2, VisitReason.VACCINATION, "Leptospirosis booster."),
        new PlannedVisit(2, "14:00", 6, VisitReason.DENTAL_CLEANING, "Resorptive lesion on the lower left premolar."),
        new PlannedVisit(2, "15:30", 3, VisitReason.SICK_VISIT, "Limping on the left hind leg."),
        new PlannedVisit(3, "08:30", 12, VisitReason.VACCINATION, "FeLV booster due."),
        new PlannedVisit(3, "10:30", 4, VisitReason.WELLNESS_EXAM, "UVB lamp and diet review."),
        new PlannedVisit(3, "12:00", 7, VisitReason.SURGERY_CONSULT, "Extraction consult for a cracked canine."),
        new PlannedVisit(3, "14:30", 1, VisitReason.SICK_VISIT, "Wet tail; possible diarrhea."),
        new PlannedVisit(3, "16:00", 11, VisitReason.IMAGING, "Hip X-rays before agility training."),
        new PlannedVisit(4, "09:00", 9, VisitReason.VACCINATION, "Bordetella before boarding."),
        new PlannedVisit(4, "10:30", 10, VisitReason.IMAGING, "Check for a swallowed seed husk."),
        new PlannedVisit(4, "13:00", 5, VisitReason.WELLNESS_EXAM, "Annual check-up."),
        new PlannedVisit(4, "15:00", 0, VisitReason.DENTAL_CLEANING, "Annual dental cleaning."),
        new PlannedVisit(4, "16:30", 3, VisitReason.SURGERY_CONSULT, "Spay consult."));

    private static final List<String> FIRST_NAMES = List.of(
        "Maya", "Theo", "Priya", "Sam", "Elena", "Jonah", "Grace", "Marcus", "Aisha", "Lukas", "Nora", "Diego");
    private static final List<String> LAST_NAMES = List.of(
        "Lindqvist", "Okafor", "Raman", "Kowalski", "Marquez", "Weiss", "Liu", "Bell", "Haddad", "Novak", "Fischer", "Tran");
    private static final List<String> STREETS = List.of(
        "Williamson St.", "Lakeview Ave.", "Regent St.", "Main St.", "Park Pl.", "Monroe St.", "Elm Dr.", "Harbor Ct.");
    private static final List<String> CITIES = List.of("Madison", "Middleton", "Verona", "Fitchburg", "Sun Prairie", "Monona");
    private static final List<List<String>> NEW_PETS = List.of(
        List.of("Pixel", "dog"), List.of("Mango", "bird"), List.of("Biscuit", "rabbit"), List.of("Juniper", "cat"),
        List.of("Nacho", "dog"), List.of("Sprout", "lizard"), List.of("Olive", "cat"), List.of("Moose", "dog"));

    private static final List<VisitReason> COMMON_REASONS = List.of(
        VisitReason.WELLNESS_EXAM, VisitReason.WELLNESS_EXAM, VisitReason.VACCINATION, VisitReason.VACCINATION,
        VisitReason.SICK_VISIT, VisitReason.SICK_VISIT, VisitReason.DENTAL_CLEANING, VisitReason.IMAGING,
        VisitReason.SURGERY_CONSULT);

    private final PetRepository pets;
    private final OwnerService ownerService;
    private final VisitScheduler scheduler;
    private final ClinicTelemetry telemetry;

    public ClinicDemo(PetRepository pets, OwnerService ownerService, VisitScheduler scheduler, ClinicTelemetry telemetry) {
        this.pets = pets;
        this.ownerService = ownerService;
        this.scheduler = scheduler;
        this.telemetry = telemetry;
    }

    /** Replaces the upcoming schedule with the sample five-day schedule. */
    public SeedResult seedVisits() {
        return telemetry.inSpan("seed sample visits", span -> {
            var removed = scheduler.clearUpcoming();
            var samplePets = pets.findAll(Sort.by("id"));
            var now = scheduler.now();
            var booked = 0;
            var skipped = 0;
            for (var planned : SAMPLE_SCHEDULE) {
                var startsAt = now.toLocalDate().plusDays(planned.day()).atTime(LocalTime.parse(planned.time()));
                if (planned.petIndex() >= samplePets.size() || !startsAt.isAfter(now)) {
                    continue;
                }
                var pet = samplePets.get(planned.petIndex());
                try {
                    scheduler.book(pet.getOwner().getId(), pet.getId(),
                        new BookVisitRequest(startsAt, planned.reason(), null, planned.notes()));
                    booked++;
                } catch (ClinicProblem problem) {
                    skipped++;
                }
            }
            log.atInfo()
                .addKeyValue("petclinic.seed.removed", removed)
                .addKeyValue("petclinic.seed.booked", booked)
                .log("Seeded the sample schedule: {} visits booked, {} upcoming visits replaced", booked, removed);
            return new SeedResult(removed, booked, skipped);
        });
    }

    /**
     * Simulates a burst of front-desk activity: a walk-in registers a pet, a handful of visits are booked (including a
     * double-booked dentist and an out-of-hours request), past-due visits are checked out, and one visit is cancelled.
     * Each action is its own trace, linked to the trace of the request that started the simulation.
     */
    public SimulationResult simulateTraffic() {
        var started = System.nanoTime();
        var trigger = Span.current().getSpanContext();
        var random = ThreadLocalRandom.current();
        var tally = new Tally();

        var today = scheduler.now().toLocalDate();
        var walkIn = WalkIn.random(today, random);
        frontDesk("simulated walk-in", trigger, tally, () -> {
            var owner = ownerService.register(walkIn.owner());
            tally.ownersRegistered++;
            ownerService.addPet(owner.getId(), walkIn.pet());
        });

        var allPets = pets.findAll(Sort.by("id"));
        for (var i = 0; i < 6; i++) {
            var pet = allPets.get(random.nextInt(allPets.size()));
            var reason = COMMON_REASONS.get(random.nextInt(COMMON_REASONS.size()));
            var startsAt = randomSlot(today, random);
            frontDesk("simulated booking", trigger, tally, () -> book(pet, reason, startsAt, tally));
        }

        // Two pets want the only dentist at the same time: the second gets 409 with the next free slot.
        var dentalSlot = randomSlot(today, random);
        var first = allPets.get(random.nextInt(allPets.size()));
        var second = allPets.get((allPets.indexOf(first) + 1 + random.nextInt(allPets.size() - 1)) % allPets.size());
        frontDesk("simulated booking", trigger, tally, () -> book(first, VisitReason.DENTAL_CLEANING, dentalSlot, tally));
        frontDesk("simulated booking", trigger, tally, () -> book(second, VisitReason.DENTAL_CLEANING, dentalSlot, tally));

        // Someone asks for a slot before the clinic opens.
        var early = allPets.get(random.nextInt(allPets.size()));
        frontDesk("simulated booking", trigger, tally,
            () -> book(early, VisitReason.VACCINATION, today.plusDays(1).atTime(7, 0), tally));

        for (var visit : scheduler.pastDue(3)) {
            frontDesk("simulated check-out", trigger, tally, () -> {
                scheduler.complete(visit.getId());
                tally.visitsCompleted++;
            });
        }

        var upcoming = scheduler.schedule(today, 3).stream()
            .filter(visit -> visit.getStatus() == VisitStatus.SCHEDULED && visit.getStartsAt().isAfter(scheduler.now()))
            .toList();
        if (!upcoming.isEmpty()) {
            var visit = upcoming.get(random.nextInt(upcoming.size()));
            frontDesk("simulated cancellation", trigger, tally, () -> {
                scheduler.cancel(visit.getId());
                tally.visitsCancelled++;
            });
        }

        var result = new SimulationResult(tally.ownersRegistered, tally.duplicateOwners, tally.visitsBooked,
            tally.slotConflicts, tally.invalidRequests, tally.visitsCompleted, tally.visitsCancelled,
            (System.nanoTime() - started) / 1_000_000);
        log.atInfo()
            .addKeyValue("petclinic.simulation.booked", result.visitsBooked())
            .addKeyValue("petclinic.simulation.conflicts", result.slotConflicts())
            .addKeyValue("petclinic.simulation.invalid", result.invalidRequests())
            .log("Simulated clinic traffic: {} visits booked, {} conflicts, {} invalid requests, {} checked out, {} cancelled",
                result.visitsBooked(), result.slotConflicts(), result.invalidRequests(), result.visitsCompleted(),
                result.visitsCancelled());
        return result;
    }

    private void book(Pet pet, VisitReason reason, LocalDateTime startsAt, Tally tally) {
        scheduler.book(pet.getOwner().getId(), pet.getId(), new BookVisitRequest(startsAt, reason, null, null));
        tally.visitsBooked++;
    }

    private void frontDesk(String name, SpanContext trigger, Tally tally, Runnable action) {
        var builder = telemetry.tracer().spanBuilder(name).setNoParent();
        if (trigger.isValid()) {
            builder.addLink(trigger);
        }
        try {
            telemetry.inSpan(builder, span -> {
                action.run();
                return null;
            });
        } catch (ClinicProblem problem) {
            switch (problem.code()) {
                case "duplicate-owner", "duplicate-pet" -> tally.duplicateOwners++;
                case "invalid-visit-time" -> tally.invalidRequests++;
                default -> tally.slotConflicts++;
            }
        }
    }

    private static LocalDateTime randomSlot(LocalDate today, ThreadLocalRandom random) {
        var slots = (VisitScheduler.LAST_SLOT.toSecondOfDay() - VisitScheduler.FIRST_SLOT.toSecondOfDay())
            / (VisitScheduler.SLOT_MINUTES * 60) + 1;
        return today.plusDays(1 + random.nextInt(10))
            .atTime(VisitScheduler.FIRST_SLOT.plusMinutes((long) VisitScheduler.SLOT_MINUTES * random.nextInt(slots)));
    }

    private record PlannedVisit(int day, String time, int petIndex, VisitReason reason, String notes) {
    }

    /** A new client with their first pet, made up from the name, street, and pet lists. */
    private record WalkIn(CreateOwnerRequest owner, CreatePetRequest pet) {
        static WalkIn random(LocalDate today, ThreadLocalRandom random) {
            var pet = NEW_PETS.get(random.nextInt(NEW_PETS.size()));
            return new WalkIn(
                new CreateOwnerRequest(
                    FIRST_NAMES.get(random.nextInt(FIRST_NAMES.size())),
                    LAST_NAMES.get(random.nextInt(LAST_NAMES.size())),
                    (100 + random.nextInt(2900)) + " " + STREETS.get(random.nextInt(STREETS.size())),
                    CITIES.get(random.nextInt(CITIES.size())),
                    "608555%04d".formatted(random.nextInt(10_000))),
                new CreatePetRequest(pet.get(0), pet.get(1), today.minusDays(60 + random.nextInt(4000))));
        }
    }

    private static final class Tally {
        int ownersRegistered;
        int duplicateOwners;
        int visitsBooked;
        int slotConflicts;
        int invalidRequests;
        int visitsCompleted;
        int visitsCancelled;
    }

    public record SeedResult(int removed, int booked, int skipped) {
    }

    public record SimulationResult(int ownersRegistered, int duplicateOwners, int visitsBooked, int slotConflicts,
        int invalidRequests, int visitsCompleted, int visitsCancelled, long durationMs) {
    }
}
