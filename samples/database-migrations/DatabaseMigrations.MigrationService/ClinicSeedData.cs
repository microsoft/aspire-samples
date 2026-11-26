using DatabaseMigrations.ApiModel;

namespace DatabaseMigrations.MigrationService;

internal static class ClinicSeedData
{
    private static readonly string[] s_reasons =
    [
        "Annual physical", "Follow-up visit", "Blood pressure check", "Skin rash consultation",
        "Knee pain assessment", "Well-child visit", "Medication review", "Post-op check",
        "Chest pain follow-up", "Mole screening", "Sports injury", "Vaccination"
    ];

    public static (List<Clinician> Clinicians, List<Patient> Patients, List<Appointment> Appointments) Create(DateTimeOffset now)
    {
        List<Clinician> clinicians =
        [
            new() { FullName = "Dr. Amara Okafor", Specialty = "Family Medicine" },
            new() { FullName = "Dr. Daniel Whitaker", Specialty = "Cardiology" },
            new() { FullName = "Dr. Priya Raman", Specialty = "Dermatology" },
            new() { FullName = "Dr. Lucas Moreau", Specialty = "Pediatrics" },
            new() { FullName = "Dr. Hannah Lindqvist", Specialty = "Orthopedics" },
            new() { FullName = "Dr. Mateo Alvarez", Specialty = "Sports Medicine" }
        ];

        List<Patient> patients =
        [
            new() { FullName = "Grace Thompson", DateOfBirth = new(1984, 3, 14), Email = "grace.thompson@example.com" },
            new() { FullName = "Omar Haddad", DateOfBirth = new(1971, 11, 2), Email = "omar.haddad@example.com" },
            new() { FullName = "Mei Chen", DateOfBirth = new(1993, 6, 27), Email = "mei.chen@example.com" },
            new() { FullName = "Samuel Osei", DateOfBirth = new(2015, 9, 8), Email = "osei.family@example.com" },
            new() { FullName = "Isabella Rossi", DateOfBirth = new(1968, 1, 19), Email = "isabella.rossi@example.com" },
            new() { FullName = "Noah Fischer", DateOfBirth = new(2001, 4, 30), Email = "noah.fischer@example.com" },
            new() { FullName = "Aisha Rahman", DateOfBirth = new(1989, 12, 5), Email = "aisha.rahman@example.com" },
            new() { FullName = "Liam O'Connor", DateOfBirth = new(1977, 7, 21), Email = "liam.oconnor@example.com" },
            new() { FullName = "Sofia Martinez", DateOfBirth = new(1996, 2, 11), Email = "sofia.martinez@example.com" },
            new() { FullName = "Kenji Watanabe", DateOfBirth = new(1958, 10, 3), Email = "kenji.watanabe@example.com" },
            new() { FullName = "Chloe Dubois", DateOfBirth = new(2012, 5, 16), Email = "dubois.family@example.com" },
            new() { FullName = "Ethan Brooks", DateOfBirth = new(1990, 8, 24), Email = "ethan.brooks@example.com" }
        ];

        // Spread appointments across the next two weeks of clinic hours, starting tomorrow at 08:00 UTC
        var firstDay = new DateTimeOffset(now.UtcDateTime.Date.AddDays(1), TimeSpan.Zero);
        var appointments = new List<Appointment>();

        for (var i = 0; i < 30; i++)
        {
            var day = firstDay.AddDays(i / 3 + (i / 15) * 2);
            var startsAt = day.AddHours(8 + (i * 5 % 9)).AddMinutes(i % 2 * 30);

            appointments.Add(new()
            {
                Clinician = clinicians[i % clinicians.Count],
                Patient = patients[(i * 7) % patients.Count],
                StartsAt = startsAt,
                DurationMinutes = i % 4 == 0 ? 45 : 30,
                Reason = s_reasons[i % s_reasons.Length]
            });
        }

        return (clinicians, patients, appointments);
    }
}
