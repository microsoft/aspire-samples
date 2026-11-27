package com.microsoft.aspire.samples.petclinic;

import java.time.LocalDateTime;
import java.util.Collection;
import java.util.List;
import java.util.Optional;

import org.springframework.data.jpa.repository.EntityGraph;
import org.springframework.data.jpa.repository.JpaRepository;
import org.springframework.data.jpa.repository.Query;

public interface VisitRepository extends JpaRepository<Visit, Long> {
    @EntityGraph(attributePaths = { "pet", "pet.owner", "vet" })
    List<Visit> findByStartsAtBetweenOrderByStartsAtAscIdAsc(LocalDateTime from, LocalDateTime to);

    @EntityGraph(attributePaths = { "pet", "pet.owner", "vet" })
    Optional<Visit> findWithDetailsById(Long id);

    @EntityGraph(attributePaths = { "pet", "pet.owner", "vet" })
    List<Visit> findByStatusAndStartsAtBetweenOrderByStartsAtAsc(VisitStatus status, LocalDateTime from, LocalDateTime to);

    List<Visit> findByVetIdInAndStatusAndStartsAtBetween(
        Collection<Long> vetIds, VisitStatus status, LocalDateTime from, LocalDateTime to);

    boolean existsByPetIdAndStartsAtAndStatus(Long petId, LocalDateTime startsAt, VisitStatus status);

    long countByStatusAndStartsAtBetween(VisitStatus status, LocalDateTime from, LocalDateTime to);

    long countByStatusNotAndStartsAtBetween(VisitStatus status, LocalDateTime from, LocalDateTime to);

    long countByStatusAndStartsAtGreaterThanEqual(VisitStatus status, LocalDateTime from);

    @EntityGraph(attributePaths = "vet")
    List<Visit> findByStatusAndStartsAtGreaterThanEqual(VisitStatus status, LocalDateTime from);

    @Query("select v.vet.specialty as specialty, count(v) as visits from Visit v where v.status = :status group by v.vet.specialty")
    List<SpecialtyCount> countBySpecialty(VisitStatus status);

    interface SpecialtyCount {
        String getSpecialty();

        long getVisits();
    }
}
