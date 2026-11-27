package com.microsoft.aspire.samples.petclinic;

import static com.microsoft.aspire.samples.petclinic.ClinicTelemetry.OWNER_CITY;
import static com.microsoft.aspire.samples.petclinic.ClinicTelemetry.OWNER_ID;
import static com.microsoft.aspire.samples.petclinic.ClinicTelemetry.PET_ID;
import static com.microsoft.aspire.samples.petclinic.ClinicTelemetry.PET_NAME;
import static com.microsoft.aspire.samples.petclinic.ClinicTelemetry.PET_SPECIES;

import io.opentelemetry.api.common.Attributes;

import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.springframework.stereotype.Service;
import org.springframework.transaction.annotation.Transactional;

import com.microsoft.aspire.samples.petclinic.ApiModels.CreateOwnerRequest;
import com.microsoft.aspire.samples.petclinic.ApiModels.CreatePetRequest;

/** Registers owners and their pets at the front desk. */
@Service
public class OwnerService {
    private static final Logger log = LoggerFactory.getLogger(OwnerService.class);

    private final OwnerRepository owners;
    private final PetRepository pets;
    private final ClinicTelemetry telemetry;

    public OwnerService(OwnerRepository owners, PetRepository pets, ClinicTelemetry telemetry) {
        this.owners = owners;
        this.pets = pets;
        this.telemetry = telemetry;
    }

    @Transactional
    public Owner register(CreateOwnerRequest request) {
        return telemetry.inSpan("register owner", span -> {
            span.setAttribute(OWNER_CITY, request.city().strip());
            var existing = owners.findFirstByFirstNameIgnoreCaseAndLastNameIgnoreCaseAndTelephone(
                request.firstName().strip(), request.lastName().strip(), request.telephone());
            if (existing.isPresent()) {
                var owner = existing.get();
                span.setAttribute(OWNER_ID, owner.getId());
                log.atWarn()
                    .addKeyValue("petclinic.owner.id", owner.getId())
                    .log("{} {} is already registered as owner {}", owner.getFirstName(), owner.getLastName(), owner.getId());
                throw ClinicProblem.duplicateOwner(owner);
            }

            var owner = owners.save(new Owner(request.firstName().strip(), request.lastName().strip(),
                request.address().strip(), request.city().strip(), request.telephone()));
            span.setAttribute(OWNER_ID, owner.getId());
            span.addEvent("owner.registered", Attributes.of(OWNER_ID, owner.getId()));
            telemetry.ownerRegistered();
            log.atInfo()
                .addKeyValue("petclinic.owner.id", owner.getId())
                .addKeyValue("petclinic.owner.city", owner.getCity())
                .log("Registered owner {} {} from {}", owner.getFirstName(), owner.getLastName(), owner.getCity());
            return owner;
        });
    }

    @Transactional
    public Pet addPet(long ownerId, CreatePetRequest request) {
        return telemetry.inSpan("add pet", span -> {
            span.setAttribute(OWNER_ID, ownerId);
            span.setAttribute(PET_SPECIES, request.type());
            var owner = owners.findWithPetsById(ownerId).orElseThrow(() -> ClinicProblem.ownerNotFound(ownerId));
            var name = request.name().strip();
            if (pets.existsByOwnerIdAndNameIgnoreCase(ownerId, name)) {
                throw ClinicProblem.duplicatePet(owner, name);
            }

            var pet = new Pet(name, request.type(), request.birthDate());
            owner.addPet(pet);
            pets.save(pet);
            span.setAttribute(PET_ID, pet.getId());
            span.addEvent("pet.registered", Attributes.of(PET_ID, pet.getId(), PET_NAME, name));
            log.atInfo()
                .addKeyValue("petclinic.owner.id", ownerId)
                .addKeyValue("petclinic.pet.id", pet.getId())
                .addKeyValue("petclinic.pet.species", pet.getType())
                .log("Registered {} the {} for {} {}", name, pet.getType(), owner.getFirstName(), owner.getLastName());
            return pet;
        });
    }
}
