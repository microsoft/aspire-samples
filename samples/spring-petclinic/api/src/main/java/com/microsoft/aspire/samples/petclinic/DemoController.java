package com.microsoft.aspire.samples.petclinic;

import io.swagger.v3.oas.annotations.Hidden;

import org.springframework.boot.autoconfigure.condition.ConditionalOnProperty;
import org.springframework.web.bind.annotation.PostMapping;
import org.springframework.web.bind.annotation.RequestMapping;
import org.springframework.web.bind.annotation.RestController;

import com.microsoft.aspire.samples.petclinic.ClinicDemo.SeedResult;
import com.microsoft.aspire.samples.petclinic.ClinicDemo.SimulationResult;

/**
 * Demo endpoints behind the Aspire dashboard's "Seed sample visits" and "Simulate clinic traffic" commands. The AppHost
 * enables them in run mode only, by setting {@code PETCLINIC_DEMO_ENABLED=true}.
 */
@Hidden
@RestController
@RequestMapping("/demo")
@ConditionalOnProperty(name = "petclinic.demo.enabled", havingValue = "true")
public class DemoController {
    private final ClinicDemo demo;

    public DemoController(ClinicDemo demo) {
        this.demo = demo;
    }

    @PostMapping("/seed")
    public SeedResult seed() {
        return demo.seedVisits();
    }

    @PostMapping("/simulate")
    public SimulationResult simulate() {
        return demo.simulateTraffic();
    }
}
