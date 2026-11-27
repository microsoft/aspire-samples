package com.microsoft.aspire.samples.petclinic;

import io.swagger.v3.oas.models.media.StringSchema;

import org.springdoc.core.customizers.OpenApiCustomizer;
import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Configuration;

@Configuration
public class OpenApiConfiguration {
    /**
     * Spring serializes a ProblemDetail's extra properties as top-level members, so document them that way instead of
     * as the nested {@code properties} object springdoc infers from the Java type.
     */
    @Bean
    OpenApiCustomizer problemDetailSchema() {
        return openApi -> {
            var schemas = openApi.getComponents() == null ? null : openApi.getComponents().getSchemas();
            var problem = schemas == null ? null : schemas.get("ProblemDetail");
            if (problem == null || problem.getProperties() == null) {
                return;
            }
            problem.getProperties().remove("properties");
            problem.addProperty("traceId", new StringSchema()
                .description("W3C trace ID of the request. Search for it in the Aspire dashboard.")
                .example("5c1f0e6d9b2a4f3e8d7c6b5a49382716"));
            problem.setAdditionalProperties(true);
            problem.setDescription("""
                RFC 9457 problem details. Problem-specific members such as `errors`, `existingOwnerId`, and \
                `nextAvailableSlot` appear next to the standard members.""");
        };
    }
}
