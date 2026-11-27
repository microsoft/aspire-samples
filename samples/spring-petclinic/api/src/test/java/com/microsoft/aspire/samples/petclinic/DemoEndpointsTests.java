package com.microsoft.aspire.samples.petclinic;

import static org.hamcrest.Matchers.greaterThan;
import static org.hamcrest.Matchers.greaterThanOrEqualTo;
import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.post;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.jsonPath;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.status;

import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.boot.webmvc.test.autoconfigure.AutoConfigureMockMvc;
import org.springframework.test.context.ActiveProfiles;
import org.springframework.test.web.servlet.MockMvc;

@SpringBootTest(properties = {
	"petclinic.demo.enabled=true",
	"spring.datasource.url=jdbc:h2:mem:petclinic-demo"
})
@AutoConfigureMockMvc
@ActiveProfiles("test")
class DemoEndpointsTests {

	@Autowired
	private MockMvc mvc;

	@Test
	void simulatedTrafficBooksVisitsAndHitsTheExpectedProblems() throws Exception {
		mvc.perform(post("/demo/simulate"))
			.andExpect(status().isOk())
			.andExpect(jsonPath("$.visitsBooked", greaterThan(0)))
			.andExpect(jsonPath("$.slotConflicts", greaterThanOrEqualTo(1)))
			.andExpect(jsonPath("$.invalidRequests").value(1));

		mvc.perform(post("/demo/seed"))
			.andExpect(status().isOk())
			.andExpect(jsonPath("$.booked", greaterThanOrEqualTo(21)));
	}
}
