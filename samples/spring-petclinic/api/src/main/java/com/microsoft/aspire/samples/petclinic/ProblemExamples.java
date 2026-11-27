package com.microsoft.aspire.samples.petclinic;

/** Example problem details responses for the OpenAPI document. */
final class ProblemExamples {
    static final String PROBLEM_JSON = "application/problem+json";

    static final String VALIDATION = """
        {"type":"https://petclinic.example/problems/validation","title":"Validation failed","status":400,\
        "detail":"One or more fields are invalid.","instance":"/api/owners",\
        "errors":{"lastName":"must not be blank","telephone":"must be 10 digits"},\
        "traceId":"5c1f0e6d9b2a4f3e8d7c6b5a49382716"}""";

    static final String INVALID_VISIT_TIME = """
        {"type":"https://petclinic.example/problems/invalid-visit-time","title":"Invalid visit time","status":400,\
        "detail":"The clinic books visits from 08:00 to 17:30, so 07:00 is outside opening hours.",\
        "instance":"/api/owners/3/pets/3/visits","traceId":"5c1f0e6d9b2a4f3e8d7c6b5a49382716"}""";

    static final String OWNER_NOT_FOUND = """
        {"type":"https://petclinic.example/problems/owner-not-found","title":"Owner not found","status":404,\
        "detail":"Owner 99 isn't registered at this clinic.","instance":"/api/owners/99",\
        "traceId":"5c1f0e6d9b2a4f3e8d7c6b5a49382716"}""";

    static final String PET_NOT_FOUND = """
        {"type":"https://petclinic.example/problems/pet-not-found","title":"Pet not found","status":404,\
        "detail":"Owner 3 has no pet with ID 12.","instance":"/api/owners/3/pets/12/visits",\
        "traceId":"5c1f0e6d9b2a4f3e8d7c6b5a49382716"}""";

    static final String VISIT_NOT_FOUND = """
        {"type":"https://petclinic.example/problems/visit-not-found","title":"Visit not found","status":404,\
        "detail":"Visit 404 doesn't exist.","instance":"/api/visits/404/cancel",\
        "traceId":"5c1f0e6d9b2a4f3e8d7c6b5a49382716"}""";

    static final String DUPLICATE_OWNER = """
        {"type":"https://petclinic.example/problems/duplicate-owner","title":"Owner already registered","status":409,\
        "detail":"George Franklin with telephone 6085551023 is already registered as owner 1.",\
        "instance":"/api/owners","existingOwnerId":1,"traceId":"5c1f0e6d9b2a4f3e8d7c6b5a49382716"}""";

    static final String DUPLICATE_PET = """
        {"type":"https://petclinic.example/problems/duplicate-pet","title":"Pet already registered","status":409,\
        "detail":"Jean Coleman already has a pet named Max.","instance":"/api/owners/6/pets",\
        "traceId":"5c1f0e6d9b2a4f3e8d7c6b5a49382716"}""";

    static final String SLOT_UNAVAILABLE = """
        {"type":"https://petclinic.example/problems/slot-unavailable","title":"Slot unavailable","status":409,\
        "detail":"No dentistry vet is free on Thu, Dec 3 at 10:30. The next free slot is Thu, Dec 3 at 11:00.",\
        "instance":"/api/owners/3/pets/3/visits","nextAvailableSlot":"2026-12-03T11:00:00",\
        "traceId":"5c1f0e6d9b2a4f3e8d7c6b5a49382716"}""";

    static final String VISIT_NOT_SCHEDULED = """
        {"type":"https://petclinic.example/problems/visit-not-scheduled","title":"Visit is no longer scheduled",\
        "status":409,"detail":"Visit 42 is already completed.","instance":"/api/visits/42/cancel",\
        "traceId":"5c1f0e6d9b2a4f3e8d7c6b5a49382716"}""";

    private ProblemExamples() {
    }
}
