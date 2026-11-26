using Microsoft.EntityFrameworkCore;

namespace DatabaseMigrations.ApiModel;

public class ClinicDbContext(DbContextOptions<ClinicDbContext> options) : DbContext(options)
{
    public DbSet<Clinician> Clinicians => Set<Clinician>();
    public DbSet<Patient> Patients => Set<Patient>();
    public DbSet<Appointment> Appointments => Set<Appointment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Clinician>(clinician =>
        {
            clinician.Property(c => c.FullName).HasMaxLength(100);
            clinician.Property(c => c.Specialty).HasMaxLength(60);
        });

        modelBuilder.Entity<Patient>(patient =>
        {
            patient.Property(p => p.FullName).HasMaxLength(100);
            patient.Property(p => p.Email).HasMaxLength(254);
            patient.HasIndex(p => p.Email).IsUnique();
        });

        modelBuilder.Entity<Appointment>(appointment =>
        {
            appointment.Property(a => a.Reason).HasMaxLength(200);
            appointment.HasOne(a => a.Clinician).WithMany(c => c.Appointments).HasForeignKey(a => a.ClinicianId);
            appointment.HasOne(a => a.Patient).WithMany(p => p.Appointments).HasForeignKey(a => a.PatientId);
            appointment.HasIndex(a => new { a.ClinicianId, a.StartsAt });
            appointment.Property(a => a.Status)
                .HasConversion<string>()
                .HasMaxLength(20)
                .HasDefaultValue(AppointmentStatus.Scheduled)
                .HasSentinel(AppointmentStatus.Scheduled);
            appointment.HasIndex(a => new { a.Status, a.StartsAt });
        });
    }
}

public class Clinician
{
    public int Id { get; set; }
    public required string FullName { get; set; }
    public required string Specialty { get; set; }
    public List<Appointment> Appointments { get; } = [];
}

public class Patient
{
    public int Id { get; set; }
    public required string FullName { get; set; }
    public DateOnly DateOfBirth { get; set; }
    public required string Email { get; set; }
    public List<Appointment> Appointments { get; } = [];
}

public class Appointment
{
    public int Id { get; set; }
    public int ClinicianId { get; set; }
    public Clinician Clinician { get; set; } = null!;
    public int PatientId { get; set; }
    public Patient Patient { get; set; } = null!;
    public DateTimeOffset StartsAt { get; set; }
    public int DurationMinutes { get; set; }
    public required string Reason { get; set; }
    public AppointmentStatus Status { get; set; }
    public DateTimeOffset? CancelledAt { get; set; }
}

public enum AppointmentStatus
{
    Scheduled,
    CheckedIn,
    Completed,
    Cancelled,
    NoShow
}
