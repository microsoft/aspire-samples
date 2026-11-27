package com.microsoft.aspire.samples.petclinic;

import static org.hamcrest.Matchers.containsString;
import static org.hamcrest.Matchers.endsWith;
import static org.hamcrest.Matchers.everyItem;
import static org.hamcrest.Matchers.hasItem;
import static org.hamcrest.Matchers.hasSize;
import static org.hamcrest.Matchers.is;
import static org.hamcrest.Matchers.startsWith;
import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.get;
import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.post;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.content;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.header;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.jsonPath;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.status;

import java.time.LocalDate;

import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.boot.webmvc.test.autoconfigure.AutoConfigureMockMvc;
import org.springframework.http.MediaType;
import org.springframework.test.context.ActiveProfiles;
import org.springframework.test.web.servlet.MockMvc;
import org.springframework.transaction.annotation.Transactional;

import com.jayway.jsonpath.JsonPath;

@SpringBootTest
@AutoConfigureMockMvc
@ActiveProfiles("test")
@Transactional
class PetclinicApiApplicationTests {
	private static final String BOOK_VISIT = "/api/owners/{ownerId}/pets/{petId}/visits";

	@Autowired
	private MockMvc mvc;

	@Test
	void scalarServesTheGeneratedApiReference() throws Exception {
		mvc.perform(get("/scalar"))
			.andExpect(status().isOk())
			.andExpect(content().contentTypeCompatibleWith(MediaType.TEXT_HTML))
			.andExpect(content().string(containsString("Spring Petclinic API")))
			.andExpect(content().string(containsString("/v3/api-docs")))
			.andExpect(content().string(containsString("/scalar/scalar.js")));

		mvc.perform(get("/scalar/scalar.js"))
			.andExpect(status().isOk());
	}

	@Test
	void openApiDocumentsOperationsAndProblemResponses() throws Exception {
		mvc.perform(get("/v3/api-docs"))
			.andExpect(status().isOk())
			.andExpect(jsonPath("$.info.title").value("Spring Petclinic API"))
			.andExpect(jsonPath("$.tags[*].name", hasItem("Visits")))
			.andExpect(jsonPath("$.paths['/api/stats'].get.summary").value("Get clinic totals"))
			.andExpect(jsonPath("$.paths['/api/owners'].get").exists())
			.andExpect(jsonPath("$.paths['/api/owners'].post.responses['201']").exists())
			.andExpect(jsonPath("$.paths['/api/owners'].post.responses['409'].content['application/problem+json']").exists())
			.andExpect(jsonPath("$.paths['/api/owners/{ownerId}/pets/{petId}/visits'].post.summary").value("Book a visit"))
			.andExpect(jsonPath("$.paths['/api/owners/{ownerId}/pets/{petId}/visits'].post.responses['409']").exists())
			.andExpect(jsonPath("$.paths['/api/vets'].get").exists())
			.andExpect(jsonPath("$.paths['/actuator/health']").doesNotExist())
			.andExpect(jsonPath("$.paths['/demo/seed']").doesNotExist())
			.andExpect(jsonPath("$.components.schemas.CreateOwnerRequest.required", hasItem("firstName")))
			.andExpect(jsonPath("$.components.schemas.BookVisitRequest.required", hasItem("startsAt")))
			.andExpect(jsonPath("$.components.schemas.ProblemDetail.properties.traceId").exists())
			.andExpect(jsonPath("$.components.schemas.ProblemDetail.properties.properties").doesNotExist());
	}

	@Test
	void sampleDataIsAvailable() throws Exception {
		mvc.perform(get("/api/stats"))
			.andExpect(status().isOk())
			.andExpect(jsonPath("$.owners").value(10))
			.andExpect(jsonPath("$.pets").value(13))
			.andExpect(jsonPath("$.vets").value(6));

		mvc.perform(get("/api/owners"))
			.andExpect(status().isOk())
			.andExpect(jsonPath("$[*].pets[*].name", hasItem("Leo")));

		mvc.perform(get("/api/owners").param("lastName", "dav"))
			.andExpect(status().isOk())
			.andExpect(jsonPath("$[*].lastName", everyItem(is("Davis"))))
			.andExpect(jsonPath("$", hasSize(2)));

		mvc.perform(get("/api/vets"))
			.andExpect(status().isOk())
			.andExpect(jsonPath("$[*].specialty", hasItem("Radiology")));

		// The sample schedule books 21 visits over the four days after today.
		mvc.perform(get("/api/visits").param("from", LocalDate.now().plusDays(1).toString()).param("days", "4"))
			.andExpect(status().isOk())
			.andExpect(jsonPath("$", hasSize(21)))
			.andExpect(jsonPath("$[0].pet.name").exists())
			.andExpect(jsonPath("$[0].vet.specialty").exists());
	}

	@Test
	void creatingAnOwnerPreservesTheExistingContract() throws Exception {
		mvc.perform(post("/api/owners")
				.contentType(MediaType.APPLICATION_JSON)
				.content("""
					{"firstName":"Alex","lastName":"Rivera","address":"123 Sample St.","city":"Madison","telephone":"6085550100"}
					"""))
			.andExpect(status().isCreated())
			.andExpect(header().string("Location", startsWith("/api/owners/")))
			.andExpect(jsonPath("$.id").isNumber())
			.andExpect(jsonPath("$.firstName").value("Alex"))
			.andExpect(jsonPath("$.pets").isEmpty());

		mvc.perform(get("/api/owners"))
			.andExpect(status().isOk())
			.andExpect(jsonPath("$[*].lastName", hasItem("Rivera")));
	}

	@Test
	void registeringAnOwnerTwiceIsAConflictProblem() throws Exception {
		mvc.perform(post("/api/owners")
				.contentType(MediaType.APPLICATION_JSON)
				.content("""
					{"firstName":"George","lastName":"Franklin","address":"110 W. Liberty St.","city":"Madison","telephone":"6085551023"}
					"""))
			.andExpect(status().isConflict())
			.andExpect(content().contentTypeCompatibleWith(MediaType.APPLICATION_PROBLEM_JSON))
			.andExpect(jsonPath("$.type", endsWith("/duplicate-owner")))
			.andExpect(jsonPath("$.existingOwnerId").isNumber())
			.andExpect(jsonPath("$.instance").value("/api/owners"));
	}

	@Test
	void invalidOwnersAreValidationProblems() throws Exception {
		mvc.perform(post("/api/owners")
				.contentType(MediaType.APPLICATION_JSON)
				.content("""
					{"firstName":"Alex","telephone":"555"}
					"""))
			.andExpect(status().isBadRequest())
			.andExpect(content().contentTypeCompatibleWith(MediaType.APPLICATION_PROBLEM_JSON))
			.andExpect(jsonPath("$.title").value("Validation failed"))
			.andExpect(jsonPath("$.errors.lastName").value("must not be blank"))
			.andExpect(jsonPath("$.errors.telephone").value("must be 10 digits"));
	}

	@Test
	void unknownOwnersAreNotFoundProblems() throws Exception {
		mvc.perform(get("/api/owners/{ownerId}", 9999))
			.andExpect(status().isNotFound())
			.andExpect(content().contentTypeCompatibleWith(MediaType.APPLICATION_PROBLEM_JSON))
			.andExpect(jsonPath("$.title").value("Owner not found"));
	}

	@Test
	void bookingAssignsAFreeVetAndSuggestsTheNextSlotWhenTaken() throws Exception {
		var leo = pet("Franklin", "Leo");
		var max = pet("Coleman", "Max");
		var tomorrow = LocalDate.now().plusDays(1);

		mvc.perform(post(BOOK_VISIT, leo[0], leo[1])
				.contentType(MediaType.APPLICATION_JSON)
				.content("""
					{"startsAt":"%sT08:00:00","reason":"DENTAL_CLEANING","notes":"Annual cleaning."}
					""".formatted(tomorrow)))
			.andExpect(status().isCreated())
			.andExpect(jsonPath("$.status").value("SCHEDULED"))
			.andExpect(jsonPath("$.vet.specialty").value("Dentistry"))
			.andExpect(jsonPath("$.pet.name").value("Leo"));

		// There's one dentist, so a second dental cleaning at the same time is a conflict.
		mvc.perform(post(BOOK_VISIT, max[0], max[1])
				.contentType(MediaType.APPLICATION_JSON)
				.content("""
					{"startsAt":"%sT08:00:00","reason":"DENTAL_CLEANING"}
					""".formatted(tomorrow)))
			.andExpect(status().isConflict())
			.andExpect(content().contentTypeCompatibleWith(MediaType.APPLICATION_PROBLEM_JSON))
			.andExpect(jsonPath("$.type", endsWith("/slot-unavailable")))
			.andExpect(jsonPath("$.nextAvailableSlot", startsWith(tomorrow + "T08:30")));
	}

	@Test
	void bookingOutsideOpeningHoursIsAnInvalidVisitTime() throws Exception {
		var leo = pet("Franklin", "Leo");

		mvc.perform(post(BOOK_VISIT, leo[0], leo[1])
				.contentType(MediaType.APPLICATION_JSON)
				.content("""
					{"startsAt":"%sT07:00:00","reason":"VACCINATION"}
					""".formatted(LocalDate.now().plusDays(1))))
			.andExpect(status().isBadRequest())
			.andExpect(jsonPath("$.type", endsWith("/invalid-visit-time")))
			.andExpect(jsonPath("$.detail", containsString("outside opening hours")));
	}

	@Test
	void bookingAnotherOwnersPetIsNotFound() throws Exception {
		var leo = pet("Franklin", "Leo");
		var max = pet("Coleman", "Max");

		mvc.perform(post(BOOK_VISIT, leo[0], max[1])
				.contentType(MediaType.APPLICATION_JSON)
				.content("""
					{"startsAt":"%sT09:00:00","reason":"VACCINATION"}
					""".formatted(LocalDate.now().plusDays(1))))
			.andExpect(status().isNotFound())
			.andExpect(jsonPath("$.type", endsWith("/pet-not-found")));
	}

	@Test
	void aCancelledVisitCantBeCancelledAgain() throws Exception {
		var sly = pet("Estaban", "Sly");
		var booked = mvc.perform(post(BOOK_VISIT, sly[0], sly[1])
				.contentType(MediaType.APPLICATION_JSON)
				.content("""
					{"startsAt":"%sT12:30:00","reason":"WELLNESS_EXAM"}
					""".formatted(LocalDate.now().plusDays(2))))
			.andExpect(status().isCreated())
			.andReturn();
		Number visitId = JsonPath.read(booked.getResponse().getContentAsString(), "$.id");

		mvc.perform(post("/api/visits/{visitId}/cancel", visitId))
			.andExpect(status().isOk())
			.andExpect(jsonPath("$.status").value("CANCELLED"));

		mvc.perform(post("/api/visits/{visitId}/cancel", visitId))
			.andExpect(status().isConflict())
			.andExpect(jsonPath("$.type", endsWith("/visit-not-scheduled")));
	}

	@Test
	void demoEndpointsAreDisabledByDefault() throws Exception {
		mvc.perform(post("/demo/simulate"))
			.andExpect(status().is4xxClientError());
	}

	private long[] pet(String ownerLastName, String petName) throws Exception {
		var json = mvc.perform(get("/api/owners").param("lastName", ownerLastName))
			.andExpect(status().isOk())
			.andReturn().getResponse().getContentAsString();
		Number ownerId = JsonPath.read(json, "$[0].id");
		Number petId = JsonPath.<java.util.List<Number>>read(json, "$[0].pets[?(@.name == '" + petName + "')].id").getFirst();
		return new long[] { ownerId.longValue(), petId.longValue() };
	}
}
