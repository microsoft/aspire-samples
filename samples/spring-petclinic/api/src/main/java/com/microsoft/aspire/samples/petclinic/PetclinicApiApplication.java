package com.microsoft.aspire.samples.petclinic;

import java.time.Clock;

import io.swagger.v3.oas.annotations.OpenAPIDefinition;
import io.swagger.v3.oas.annotations.info.Info;
import io.swagger.v3.oas.annotations.tags.Tag;

import org.springframework.boot.SpringApplication;
import org.springframework.boot.autoconfigure.SpringBootApplication;
import org.springframework.context.annotation.Bean;

@SpringBootApplication
@OpenAPIDefinition(
	info = @Info(
		title = "Spring Petclinic API",
		version = "v1",
		description = """
			Front-desk API for a small veterinary clinic: register pet owners and their pets, look up the veterinary \
			team, and book pets into 30-minute visits with a vet of the right specialty.

			Errors are [RFC 9457](https://www.rfc-editor.org/rfc/rfc9457) problem details \
			(`application/problem+json`). Validation problems list each invalid field under `errors`, and every \
			problem includes the request's `traceId`, so you can open the trace in the Aspire dashboard.

			This is a local demo: the API has no authentication. Use synthetic data only."""),
	tags = {
		@Tag(name = "Owners", description = "Pet owners and their pets."),
		@Tag(name = "Visits", description = "The visit schedule: book, complete, and cancel visits."),
		@Tag(name = "Clinic", description = "Clinic totals, the veterinary team, and the reasons visits can be booked for.")
	})
public class PetclinicApiApplication {

	public static void main(String[] args) {
		SpringApplication.run(PetclinicApiApplication.class, args);
	}

	@Bean
	Clock clock() {
		return Clock.systemDefaultZone();
	}

}
