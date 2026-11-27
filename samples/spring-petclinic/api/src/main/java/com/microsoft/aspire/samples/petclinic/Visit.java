package com.microsoft.aspire.samples.petclinic;

import java.time.LocalDateTime;

import jakarta.persistence.Entity;
import jakarta.persistence.EnumType;
import jakarta.persistence.Enumerated;
import jakarta.persistence.FetchType;
import jakarta.persistence.GeneratedValue;
import jakarta.persistence.GenerationType;
import jakarta.persistence.Id;
import jakarta.persistence.Index;
import jakarta.persistence.JoinColumn;
import jakarta.persistence.ManyToOne;
import jakarta.persistence.Table;

@Entity
@Table(name = "visits", indexes = @Index(name = "ix_visits_starts_at", columnList = "startsAt"))
public class Visit {
    @Id
    @GeneratedValue(strategy = GenerationType.IDENTITY)
    private Long id;

    @ManyToOne(fetch = FetchType.LAZY, optional = false)
    @JoinColumn(name = "pet_id")
    private Pet pet;

    @ManyToOne(fetch = FetchType.LAZY, optional = false)
    @JoinColumn(name = "vet_id")
    private Vet vet;

    private LocalDateTime startsAt;

    @Enumerated(EnumType.STRING)
    private VisitReason reason;

    private String notes;

    @Enumerated(EnumType.STRING)
    private VisitStatus status = VisitStatus.SCHEDULED;

    protected Visit() {
    }

    public Visit(Pet pet, Vet vet, LocalDateTime startsAt, VisitReason reason, String notes) {
        this.pet = pet;
        this.vet = vet;
        this.startsAt = startsAt;
        this.reason = reason;
        this.notes = notes;
    }

    public Long getId() {
        return id;
    }

    public Pet getPet() {
        return pet;
    }

    public Vet getVet() {
        return vet;
    }

    public LocalDateTime getStartsAt() {
        return startsAt;
    }

    public VisitReason getReason() {
        return reason;
    }

    public String getNotes() {
        return notes;
    }

    public VisitStatus getStatus() {
        return status;
    }

    void setStatus(VisitStatus status) {
        this.status = status;
    }
}
