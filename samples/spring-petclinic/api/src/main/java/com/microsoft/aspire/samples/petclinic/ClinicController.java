package com.microsoft.aspire.samples.petclinic;

import java.util.Arrays;
import java.util.List;

import io.swagger.v3.oas.annotations.Operation;
import io.swagger.v3.oas.annotations.tags.Tag;

import org.springframework.web.bind.annotation.GetMapping;
import org.springframework.web.bind.annotation.RequestMapping;
import org.springframework.web.bind.annotation.RestController;

import com.microsoft.aspire.samples.petclinic.ApiModels.StatsResponse;
import com.microsoft.aspire.samples.petclinic.ApiModels.VetResponse;
import com.microsoft.aspire.samples.petclinic.ApiModels.VisitReasonResponse;

@RestController
@RequestMapping("/api")
@Tag(name = "Clinic")
public class ClinicController {
    private final VetRepository vets;
    private final VisitScheduler scheduler;

    public ClinicController(VetRepository vets, VisitScheduler scheduler) {
        this.vets = vets;
        this.scheduler = scheduler;
    }

    @GetMapping("/stats")
    @Operation(operationId = "getStats", summary = "Get clinic totals",
        description = "Counts owners, pets, and vets, today's visits that aren't cancelled, and scheduled visits from now on.")
    public StatsResponse stats() {
        return scheduler.stats();
    }

    @GetMapping("/vets")
    @Operation(operationId = "listVets", summary = "List veterinarians",
        description = "Returns the veterinary team, sorted by last name, with each vet's specialty.")
    public List<VetResponse> vets() {
        return vets.findAllByOrderByLastNameAscFirstNameAsc().stream().map(VetResponse::from).toList();
    }

    @GetMapping("/visit-reasons")
    @Operation(operationId = "listVisitReasons", summary = "List visit reasons",
        description = "Returns the reasons a visit can be booked for and the vet specialty each one needs.")
    public List<VisitReasonResponse> visitReasons() {
        return Arrays.stream(VisitReason.values())
            .map(reason -> new VisitReasonResponse(reason, reason.label(), reason.specialty()))
            .toList();
    }
}
