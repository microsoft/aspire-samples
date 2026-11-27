using Microsoft.EntityFrameworkCore;
using Api.Models;

namespace Api.Data;

public class ServiceDeskDbContext(DbContextOptions<ServiceDeskDbContext> options) : DbContext(options)
{
    public DbSet<ServiceRequest> Requests => Set<ServiceRequest>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ServiceRequest>(entity =>
        {
            entity.ToTable("ServiceRequests");
            entity.HasKey(e => e.Id);
            // Ticket numbers start at SR-1001.
            entity.Property(e => e.Id).HasIdentityOptions(startValue: 1001);
            entity.Property(e => e.Title).IsRequired().HasMaxLength(ServiceRequest.MaxTitleLength);
            entity.Property(e => e.RequestedBy).IsRequired().HasMaxLength(ServiceRequest.MaxPersonNameLength);
            entity.Property(e => e.AssignedTo).HasMaxLength(ServiceRequest.MaxPersonNameLength);
            entity.Ignore(e => e.IsOpen);
            entity.HasIndex(e => new { e.Status, e.Priority, e.DueAt });
        });
    }
}