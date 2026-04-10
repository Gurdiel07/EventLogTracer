using EventLogTracer.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EventLogTracer.Infrastructure.Data.Configurations;

public class EventCorrelationConfiguration : IEntityTypeConfiguration<EventCorrelation>
{
    public void Configure(EntityTypeBuilder<EventCorrelation> builder)
    {
        builder.ToTable("EventCorrelations");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.Id)
            .ValueGeneratedNever();

        builder.Property(c => c.Name)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(c => c.DetectedAt)
            .IsRequired();

        builder.Property(c => c.CorrelationType)
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(c => c.Description)
            .HasMaxLength(2048);

        // Many-to-many with EventEntry via shadow join table
        builder.HasMany(c => c.EventEntries)
            .WithMany()
            .UsingEntity(j => j.ToTable("EventCorrelationEntries"));
    }
}
