package com.microsoft.aspire.samples.petclinic;

import java.time.LocalDate;
import java.time.LocalDateTime;
import java.util.List;

import io.swagger.v3.oas.annotations.media.Schema;
import jakarta.validation.constraints.NotBlank;
import jakarta.validation.constraints.NotNull;
import jakarta.validation.constraints.PastOrPresent;
import jakarta.validation.constraints.Pattern;
import jakarta.validation.constraints.Size;

/** Request and response bodies of the Petclinic API. */
public final class ApiModels {
    static final String SPECIES = "cat|dog|bird|hamster|lizard|snake|rabbit";

    private ApiModels() {
    }

    @Schema(description = "Clinic totals for the front desk dashboard.")
    public record StatsResponse(
        @Schema(description = "Registered pet owners.", example = "10") long owners,
        @Schema(description = "Registered pets.", example = "13") long pets,
        @Schema(description = "Veterinarians on the team.", example = "6") long vets,
        @Schema(description = "Visits still scheduled for today.", example = "4") long visitsToday,
        @Schema(description = "Scheduled visits from now on.", example = "21") long upcomingVisits) {
    }

    @Schema(description = "A pet owner and their pets.")
    public record OwnerResponse(
        @Schema(example = "1") Long id,
        @Schema(example = "George") String firstName,
        @Schema(example = "Franklin") String lastName,
        @Schema(example = "110 W. Liberty St.") String address,
        @Schema(example = "Madison") String city,
        @Schema(description = "Ten-digit telephone number.", example = "6085551023") String telephone,
        List<PetResponse> pets) {

        static OwnerResponse from(Owner owner) {
            return new OwnerResponse(owner.getId(), owner.getFirstName(), owner.getLastName(), owner.getAddress(),
                owner.getCity(), owner.getTelephone(), owner.getPets().stream().map(PetResponse::from).toList());
        }
    }

    @Schema(description = "A pet registered at the clinic.")
    public record PetResponse(
        @Schema(example = "1") Long id,
        @Schema(example = "Leo") String name,
        @Schema(description = "Species.", allowableValues = { "cat", "dog", "bird", "hamster", "lizard", "snake", "rabbit" },
            example = "cat") String type,
        @Schema(example = "2021-09-07") LocalDate birthDate) {

        static PetResponse from(Pet pet) {
            return new PetResponse(pet.getId(), pet.getName(), pet.getType(), pet.getBirthDate());
        }
    }

    @Schema(description = "A veterinarian and their specialty.")
    public record VetResponse(
        @Schema(example = "3") Long id,
        @Schema(example = "Linda") String firstName,
        @Schema(example = "Douglas") String lastName,
        @Schema(allowableValues = { Vet.GENERAL_PRACTICE, Vet.DENTISTRY, Vet.RADIOLOGY, Vet.SURGERY },
            example = Vet.DENTISTRY) String specialty) {

        static VetResponse from(Vet vet) {
            return new VetResponse(vet.getId(), vet.getFirstName(), vet.getLastName(), vet.getSpecialty());
        }
    }

    @Schema(description = "A new pet owner. Owners are unique by first name, last name, and telephone.")
    public record CreateOwnerRequest(
        @NotBlank @Size(max = 40) @Schema(example = "Maya") String firstName,
        @NotBlank @Size(max = 40) @Schema(example = "Lindqvist") String lastName,
        @NotBlank @Size(max = 80) @Schema(example = "1420 Willy St.") String address,
        @NotBlank @Size(max = 40) @Schema(example = "Madison") String city,
        @NotBlank @Pattern(regexp = "\\d{10}", message = "must be 10 digits")
        @Schema(description = "Ten-digit telephone number.", example = "6085550147") String telephone) {
    }

    @Schema(description = "A new pet for an existing owner. Pet names are unique per owner.")
    public record CreatePetRequest(
        @NotBlank @Size(max = 30) @Schema(example = "Pixel") String name,
        @NotBlank @Pattern(regexp = SPECIES, message = "must be one of cat, dog, bird, hamster, lizard, snake, rabbit")
        @Schema(allowableValues = { "cat", "dog", "bird", "hamster", "lizard", "snake", "rabbit" }, example = "dog") String type,
        @NotNull @PastOrPresent @Schema(example = "2024-05-18") LocalDate birthDate) {
    }

    @Schema(description = """
        A visit to book. Visits start on the hour or half hour between 08:00 and 17:30, in the future, and at most \
        60 days ahead. Leave out `vetId` to book the first free vet with the specialty the reason needs.""")
    public record BookVisitRequest(
        @NotNull @Schema(description = "Local clinic time the 30-minute visit starts.", example = "2026-12-03T10:30:00")
        LocalDateTime startsAt,
        @NotNull @Schema(example = "DENTAL_CLEANING") VisitReason reason,
        @Schema(description = "A specific vet to book. Omit to assign one automatically.", example = "3")
        Long vetId,
        @Size(max = 280) @Schema(description = "Notes for the vet.", example = "Tartar on the upper molars; eats on one side.")
        String notes) {
    }

    @Schema(description = "A pet's visit with a vet.")
    public record VisitResponse(
        @Schema(example = "42") Long id,
        @Schema(description = "Local clinic time the 30-minute visit starts.", example = "2026-12-03T10:30:00")
        LocalDateTime startsAt,
        @Schema(example = "DENTAL_CLEANING") VisitReason reason,
        @Schema(example = "Dental cleaning") String reasonLabel,
        @Schema(example = "SCHEDULED") VisitStatus status,
        @Schema(example = "Tartar on the upper molars; eats on one side.") String notes,
        PetSummary pet,
        OwnerSummary owner,
        VetResponse vet) {

        static VisitResponse from(Visit visit) {
            var pet = visit.getPet();
            var owner = pet.getOwner();
            return new VisitResponse(visit.getId(), visit.getStartsAt(), visit.getReason(), visit.getReason().label(),
                visit.getStatus(), visit.getNotes(),
                new PetSummary(pet.getId(), pet.getName(), pet.getType()),
                new OwnerSummary(owner.getId(), owner.getFirstName(), owner.getLastName(), owner.getTelephone()),
                VetResponse.from(visit.getVet()));
        }
    }

    @Schema(description = "The pet a visit is for.")
    public record PetSummary(@Schema(example = "3") Long id, @Schema(example = "Rosy") String name,
        @Schema(example = "dog") String type) {
    }

    @Schema(description = "The owner of the pet a visit is for.")
    public record OwnerSummary(@Schema(example = "3") Long id, @Schema(example = "Eduardo") String firstName,
        @Schema(example = "Rodriquez") String lastName, @Schema(example = "6085558763") String telephone) {
    }

    @Schema(description = "A reason a visit can be booked for, and the specialty it needs.")
    public record VisitReasonResponse(
        @Schema(example = "DENTAL_CLEANING") VisitReason reason,
        @Schema(example = "Dental cleaning") String label,
        @Schema(example = Vet.DENTISTRY) String specialty) {
    }
}
