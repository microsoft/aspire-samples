package com.microsoft.aspire.samples.petclinic;

import java.time.LocalDate;
import java.util.List;

import org.springframework.boot.CommandLineRunner;
import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Configuration;

@Configuration
public class SampleDataConfiguration {
    @Bean
    CommandLineRunner loadSampleData(OwnerRepository owners, VetRepository vets, VisitRepository visits,
        VisitScheduler scheduler, ClinicDemo demo) {
        return args -> {
            if (owners.count() == 0) {
                owners.saveAll(List.of(
                    owner("George", "Franklin", "110 W. Liberty St.", "Madison", "6085551023",
                        new Pet("Leo", "cat", LocalDate.of(2020, 9, 7))),
                    owner("Betty", "Davis", "638 Cardinal Ave.", "Sun Prairie", "6085551749",
                        new Pet("Basil", "hamster", LocalDate.of(2024, 8, 6))),
                    owner("Eduardo", "Rodriquez", "2693 Commerce St.", "McFarland", "6085558763",
                        new Pet("Rosy", "dog", LocalDate.of(2019, 4, 17)),
                        new Pet("Jewel", "dog", LocalDate.of(2021, 3, 7))),
                    owner("Harold", "Davis", "563 Friendly St.", "Windsor", "6085553198",
                        new Pet("Iggy", "lizard", LocalDate.of(2022, 11, 30))),
                    owner("Peter", "McTavish", "2387 S. Fair Way", "Madison", "6085552765",
                        new Pet("George", "snake", LocalDate.of(2018, 1, 20))),
                    owner("Jean", "Coleman", "105 N. Lake St.", "Monona", "6085552654",
                        new Pet("Samantha", "cat", LocalDate.of(2017, 9, 4)),
                        new Pet("Max", "cat", LocalDate.of(2017, 9, 4))),
                    owner("Jeff", "Black", "1450 Oak Blvd.", "Monona", "6085555387",
                        new Pet("Lucky", "bird", LocalDate.of(2023, 8, 6))),
                    owner("Maria", "Escobito", "345 Maple St.", "Madison", "6085557683",
                        new Pet("Mulligan", "dog", LocalDate.of(2015, 2, 24))),
                    owner("David", "Schroeder", "2749 Blackhawk Trail", "Madison", "6085559435",
                        new Pet("Freddy", "bird", LocalDate.of(2022, 3, 9))),
                    owner("Carlos", "Estaban", "2335 Independence La.", "Waunakee", "6085555487",
                        new Pet("Lucky", "dog", LocalDate.of(2020, 6, 24)),
                        new Pet("Sly", "cat", LocalDate.of(2022, 6, 8)))));
            }

            if (vets.count() == 0) {
                vets.saveAll(List.of(
                    new Vet("James", "Carter", Vet.GENERAL_PRACTICE),
                    new Vet("Helen", "Leary", Vet.RADIOLOGY),
                    new Vet("Linda", "Douglas", Vet.DENTISTRY),
                    new Vet("Rafael", "Ortega", Vet.SURGERY),
                    new Vet("Henry", "Stevens", Vet.RADIOLOGY),
                    new Vet("Sharon", "Jenkins", Vet.GENERAL_PRACTICE)));
            }

            if (visits.count() == 0) {
                demo.seedVisits();
            } else {
                scheduler.reportScheduledVisits();
            }
        };
    }

    private static Owner owner(String firstName, String lastName, String address, String city, String telephone,
        Pet... pets) {
        var owner = new Owner(firstName, lastName, address, city, telephone);
        for (var pet : pets) {
            owner.addPet(pet);
        }
        return owner;
    }
}
