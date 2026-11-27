package com.microsoft.aspire.samples.petclinic;

import io.swagger.v3.oas.annotations.media.Schema;

/** Why a pet is coming in, and which specialty sees it. */
@Schema(description = "Reason for the visit. It decides which veterinary specialty is booked.")
public enum VisitReason {
    WELLNESS_EXAM("Wellness exam", Vet.GENERAL_PRACTICE),
    VACCINATION("Vaccination", Vet.GENERAL_PRACTICE),
    SICK_VISIT("Sick visit", Vet.GENERAL_PRACTICE),
    DENTAL_CLEANING("Dental cleaning", Vet.DENTISTRY),
    IMAGING("X-ray and imaging", Vet.RADIOLOGY),
    SURGERY_CONSULT("Surgery consult", Vet.SURGERY);

    private final String label;
    private final String specialty;

    VisitReason(String label, String specialty) {
        this.label = label;
        this.specialty = specialty;
    }

    public String label() {
        return label;
    }

    public String specialty() {
        return specialty;
    }
}
