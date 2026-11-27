package com.microsoft.aspire.samples.petclinic;

import java.net.URI;
import java.time.LocalDateTime;
import java.time.format.DateTimeFormatter;
import java.util.Locale;

import org.springframework.http.HttpStatus;
import org.springframework.http.ProblemDetail;
import org.springframework.web.ErrorResponseException;

/**
 * A domain error that Spring MVC renders as an RFC 9457 problem details response ({@code application/problem+json}).
 * {@link ProblemDetailsAdvice} adds the request path and trace ID.
 */
public class ClinicProblem extends ErrorResponseException {
    static final String TYPE_PREFIX = "https://petclinic.example/problems/";
    static final DateTimeFormatter SLOT_FORMAT = DateTimeFormatter.ofPattern("EEE, MMM d 'at' HH:mm", Locale.US);

    private final String code;

    private ClinicProblem(HttpStatus status, String code, String title, String detail) {
        super(status, problem(status, code, title, detail), null);
        this.code = code;
    }

    /** The problem type's short name, for example {@code slot-unavailable}. Used as a span and log attribute. */
    public String code() {
        return code;
    }

    private ClinicProblem with(String property, Object value) {
        getBody().setProperty(property, value);
        return this;
    }

    private static ProblemDetail problem(HttpStatus status, String code, String title, String detail) {
        var problem = ProblemDetail.forStatusAndDetail(status, detail);
        problem.setType(URI.create(TYPE_PREFIX + code));
        problem.setTitle(title);
        return problem;
    }

    static ClinicProblem ownerNotFound(long ownerId) {
        return new ClinicProblem(HttpStatus.NOT_FOUND, "owner-not-found", "Owner not found",
            "Owner %d isn't registered at this clinic.".formatted(ownerId));
    }

    static ClinicProblem petNotFound(long ownerId, long petId) {
        return new ClinicProblem(HttpStatus.NOT_FOUND, "pet-not-found", "Pet not found",
            "Owner %d has no pet with ID %d.".formatted(ownerId, petId));
    }

    static ClinicProblem vetNotFound(long vetId) {
        return new ClinicProblem(HttpStatus.NOT_FOUND, "vet-not-found", "Vet not found",
            "There's no vet with ID %d on the team.".formatted(vetId));
    }

    static ClinicProblem visitNotFound(long visitId) {
        return new ClinicProblem(HttpStatus.NOT_FOUND, "visit-not-found", "Visit not found",
            "Visit %d doesn't exist.".formatted(visitId));
    }

    static ClinicProblem duplicateOwner(Owner existing) {
        return new ClinicProblem(HttpStatus.CONFLICT, "duplicate-owner", "Owner already registered",
            "%s %s with telephone %s is already registered as owner %d."
                .formatted(existing.getFirstName(), existing.getLastName(), existing.getTelephone(), existing.getId()))
            .with("existingOwnerId", existing.getId());
    }

    static ClinicProblem duplicatePet(Owner owner, String petName) {
        return new ClinicProblem(HttpStatus.CONFLICT, "duplicate-pet", "Pet already registered",
            "%s %s already has a pet named %s.".formatted(owner.getFirstName(), owner.getLastName(), petName));
    }

    static ClinicProblem invalidVisitTime(String detail) {
        return new ClinicProblem(HttpStatus.BAD_REQUEST, "invalid-visit-time", "Invalid visit time", detail);
    }

    static ClinicProblem petAlreadyBooked(Pet pet, LocalDateTime startsAt) {
        return new ClinicProblem(HttpStatus.CONFLICT, "pet-already-booked", "Pet already booked",
            "%s already has a visit on %s.".formatted(pet.getName(), SLOT_FORMAT.format(startsAt)));
    }

    /** {@code busy} is the first sentence of the detail, for example "No Dentistry vet is free on Thu, Dec 3 at 10:30." */
    static ClinicProblem slotUnavailable(String busy, LocalDateTime nextAvailable) {
        if (nextAvailable == null) {
            return new ClinicProblem(HttpStatus.CONFLICT, "slot-unavailable", "Slot unavailable",
                busy + " There's no free slot in the next three days.");
        }
        return new ClinicProblem(HttpStatus.CONFLICT, "slot-unavailable", "Slot unavailable",
            busy + " The next free slot is %s.".formatted(SLOT_FORMAT.format(nextAvailable)))
            .with("nextAvailableSlot", nextAvailable);
    }

    static ClinicProblem visitNotScheduled(Visit visit) {
        return new ClinicProblem(HttpStatus.CONFLICT, "visit-not-scheduled", "Visit is no longer scheduled",
            "Visit %d is already %s.".formatted(visit.getId(), visit.getStatus().name().toLowerCase()));
    }
}
