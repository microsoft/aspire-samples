package com.microsoft.aspire.samples.petclinic;

import io.swagger.v3.oas.annotations.media.Schema;

@Schema(description = "Where the visit is in its lifecycle. Only `SCHEDULED` visits can be completed or cancelled.")
public enum VisitStatus {
    SCHEDULED,
    COMPLETED,
    CANCELLED
}
