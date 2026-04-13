using Microsoft.EntityFrameworkCore;
using Worker.Models;

namespace Worker.Data;

public class ImageDbContext(DbContextOptions<ImageDbContext> options) : DbContext(options)
{
    public DbSet<Image> Images => Set<Image>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Image>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.FileName).IsRequired().HasMaxLength(500);
            entity.Property(e => e.ContentType).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Size).IsRequired();
            entity.Property(e => e.BlobUrl).IsRequired().HasMaxLength(1000);
            entity.Property(e => e.ThumbnailUrl).HasMaxLength(1000);
            entity.Property(e => e.ThumbnailProcessed).IsRequired();
            entity.Property(e => e.UploadedAt).IsRequired();
        });
    }
}
