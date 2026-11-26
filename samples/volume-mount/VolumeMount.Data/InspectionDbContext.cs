using Microsoft.EntityFrameworkCore;

namespace VolumeMount.Data;

public class InspectionDbContext(DbContextOptions<InspectionDbContext> options) : DbContext(options)
{
    public DbSet<Inspection> Inspections => Set<Inspection>();

    public DbSet<InspectionAttachment> Attachments => Set<InspectionAttachment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Inspection>(inspection =>
        {
            inspection.Property(i => i.AssetTag).HasMaxLength(32);
            inspection.Property(i => i.AssetName).HasMaxLength(120);
            inspection.Property(i => i.Site).HasMaxLength(80);
            inspection.Property(i => i.Inspector).HasMaxLength(80);
            inspection.Property(i => i.Notes).HasMaxLength(2000);
            inspection.Property(i => i.Outcome).HasConversion<string>().HasMaxLength(16);
            inspection.HasIndex(i => i.InspectedAt);
            inspection.HasIndex(i => i.AssetTag);
            inspection.HasMany(i => i.Attachments).WithOne().HasForeignKey(a => a.InspectionId);
        });

        modelBuilder.Entity<InspectionAttachment>(attachment =>
        {
            attachment.Property(a => a.FileName).HasMaxLength(260);
            attachment.Property(a => a.ContentType).HasMaxLength(100);
            attachment.Property(a => a.BlobName).HasMaxLength(400);
        });
    }
}
