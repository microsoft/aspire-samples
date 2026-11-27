package com.microsoft.aspire.samples.petclinic;

import static com.microsoft.aspire.samples.petclinic.ProblemExamples.PROBLEM_JSON;

import java.net.URI;
import java.util.List;

import io.swagger.v3.oas.annotations.Operation;
import io.swagger.v3.oas.annotations.Parameter;
import io.swagger.v3.oas.annotations.media.Content;
import io.swagger.v3.oas.annotations.media.ExampleObject;
import io.swagger.v3.oas.annotations.media.Schema;
import io.swagger.v3.oas.annotations.responses.ApiResponse;
import io.swagger.v3.oas.annotations.tags.Tag;
import jakarta.validation.Valid;

import org.springframework.http.HttpStatus;
import org.springframework.http.ProblemDetail;
import org.springframework.http.ResponseEntity;
import org.springframework.web.bind.annotation.GetMapping;
import org.springframework.web.bind.annotation.PathVariable;
import org.springframework.web.bind.annotation.PostMapping;
import org.springframework.web.bind.annotation.RequestBody;
import org.springframework.web.bind.annotation.RequestMapping;
import org.springframework.web.bind.annotation.RequestParam;
import org.springframework.web.bind.annotation.ResponseStatus;
import org.springframework.web.bind.annotation.RestController;

import com.microsoft.aspire.samples.petclinic.ApiModels.CreateOwnerRequest;
import com.microsoft.aspire.samples.petclinic.ApiModels.CreatePetRequest;
import com.microsoft.aspire.samples.petclinic.ApiModels.OwnerResponse;
import com.microsoft.aspire.samples.petclinic.ApiModels.PetResponse;

// Trusted local demo: these unauthenticated routes expose all owner records and allow owner creation.
// Use synthetic data only, and do not expose or forward this API to untrusted networks.
@RestController
@RequestMapping("/api/owners")
@Tag(name = "Owners")
public class OwnersController {
    private final OwnerRepository owners;
    private final OwnerService ownerService;

    public OwnersController(OwnerRepository owners, OwnerService ownerService) {
        this.owners = owners;
        this.ownerService = ownerService;
    }

    @GetMapping
    @Operation(operationId = "listOwners", summary = "List owners",
        description = "Returns pet owners and their pets, sorted by last name. Filter with a case-insensitive last-name prefix.")
    public List<OwnerResponse> owners(
        @Parameter(description = "Case-insensitive prefix of the owner's last name.", example = "Dav")
        @RequestParam(required = false) String lastName) {
        var result = lastName == null || lastName.isBlank()
            ? owners.findAllByOrderByLastNameAscFirstNameAsc()
            : owners.findByLastNameStartingWithIgnoreCaseOrderByLastNameAscFirstNameAsc(lastName.strip());
        return result.stream().map(OwnerResponse::from).toList();
    }

    @GetMapping("/{ownerId}")
    @Operation(operationId = "getOwner", summary = "Get an owner", description = "Returns one owner and their pets.")
    @ApiResponse(responseCode = "200", description = "The owner and their pets.")
    @ApiResponse(responseCode = "404", description = "No owner has this ID.", content = @Content(mediaType = PROBLEM_JSON,
        schema = @Schema(implementation = ProblemDetail.class), examples = @ExampleObject(value = ProblemExamples.OWNER_NOT_FOUND)))
    public OwnerResponse owner(@Parameter(description = "Owner ID.", example = "1") @PathVariable long ownerId) {
        return owners.findWithPetsById(ownerId).map(OwnerResponse::from)
            .orElseThrow(() -> ClinicProblem.ownerNotFound(ownerId));
    }

    @PostMapping
    @ResponseStatus(HttpStatus.CREATED)
    @Operation(operationId = "registerOwner", summary = "Register an owner", description = """
        Registers a new pet owner. All fields are required and the telephone number is ten digits. Registering the \
        same first name, last name, and telephone number twice returns `409 Conflict` with the existing owner's ID.""")
    @io.swagger.v3.oas.annotations.parameters.RequestBody(content = @Content(examples = @ExampleObject(name = "New owner",
        value = """
            {"firstName":"Maya","lastName":"Lindqvist","address":"1420 Williamson St.","city":"Madison","telephone":"6085550147"}""")))
    @ApiResponse(responseCode = "201", description = "The owner was registered. `Location` points at the new owner.")
    @ApiResponse(responseCode = "400", description = "A field is missing or invalid.", content = @Content(mediaType = PROBLEM_JSON,
        schema = @Schema(implementation = ProblemDetail.class), examples = @ExampleObject(value = ProblemExamples.VALIDATION)))
    @ApiResponse(responseCode = "409", description = "The owner is already registered.", content = @Content(mediaType = PROBLEM_JSON,
        schema = @Schema(implementation = ProblemDetail.class), examples = @ExampleObject(value = ProblemExamples.DUPLICATE_OWNER)))
    public ResponseEntity<OwnerResponse> createOwner(@Valid @RequestBody CreateOwnerRequest request) {
        var owner = ownerService.register(request);
        return ResponseEntity.created(URI.create("/api/owners/" + owner.getId())).body(OwnerResponse.from(owner));
    }

    @PostMapping("/{ownerId}/pets")
    @ResponseStatus(HttpStatus.CREATED)
    @Operation(operationId = "addPet", summary = "Add a pet", description = """
        Registers a pet for an existing owner. The species is one of `cat`, `dog`, `bird`, `hamster`, `lizard`, `snake`, or \
        `rabbit`, the birth date can't be in the future, and an owner can't have two pets with the same name.""")
    @io.swagger.v3.oas.annotations.parameters.RequestBody(content = @Content(examples = @ExampleObject(name = "New puppy",
        value = """
            {"name":"Pixel","type":"dog","birthDate":"2024-05-18"}""")))
    @ApiResponse(responseCode = "201", description = "The pet was registered.")
    @ApiResponse(responseCode = "400", description = "A field is missing or invalid.", content = @Content(mediaType = PROBLEM_JSON,
        schema = @Schema(implementation = ProblemDetail.class), examples = @ExampleObject(value = ProblemExamples.VALIDATION)))
    @ApiResponse(responseCode = "404", description = "No owner has this ID.", content = @Content(mediaType = PROBLEM_JSON,
        schema = @Schema(implementation = ProblemDetail.class), examples = @ExampleObject(value = ProblemExamples.OWNER_NOT_FOUND)))
    @ApiResponse(responseCode = "409", description = "The owner already has a pet with this name.", content = @Content(
        mediaType = PROBLEM_JSON, schema = @Schema(implementation = ProblemDetail.class),
        examples = @ExampleObject(value = ProblemExamples.DUPLICATE_PET)))
    public ResponseEntity<PetResponse> addPet(@Parameter(description = "Owner ID.", example = "1") @PathVariable long ownerId,
        @Valid @RequestBody CreatePetRequest request) {
        var pet = ownerService.addPet(ownerId, request);
        return ResponseEntity.created(URI.create("/api/owners/" + ownerId)).body(PetResponse.from(pet));
    }
}
