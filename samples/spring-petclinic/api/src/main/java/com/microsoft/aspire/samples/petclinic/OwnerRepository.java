package com.microsoft.aspire.samples.petclinic;

import java.util.List;
import java.util.Optional;

import org.springframework.data.jpa.repository.EntityGraph;
import org.springframework.data.jpa.repository.JpaRepository;

public interface OwnerRepository extends JpaRepository<Owner, Long> {
    @EntityGraph(attributePaths = "pets")
    List<Owner> findAllByOrderByLastNameAscFirstNameAsc();

    @EntityGraph(attributePaths = "pets")
    List<Owner> findByLastNameStartingWithIgnoreCaseOrderByLastNameAscFirstNameAsc(String lastName);

    @EntityGraph(attributePaths = "pets")
    Optional<Owner> findWithPetsById(Long id);

    Optional<Owner> findFirstByFirstNameIgnoreCaseAndLastNameIgnoreCaseAndTelephone(
        String firstName, String lastName, String telephone);
}
