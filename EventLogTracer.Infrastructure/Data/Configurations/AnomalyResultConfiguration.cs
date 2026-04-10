using EventLogTracer.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EventLogTracer.Infrastructure.Data.Configurations;

public class AnomalyResultConfiguration : IEntityTypeConfiguration<AnomalyResult>
{
    public void Configure(EntityTypeBuilder<AnomalyResult> builder)
    {
        builder.ToTable("AnomalyResults");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.Id)
            .ValueGeneratedNever();

        builder.Property(a => a.DetectedAt)
            .IsRequired();

        builder.Property(a => a.Severity)
            .IsRequired()
            .HasConversion<string>();

        builder.Property(a => a.Description)
            .HasMaxLength(2048);

        builder.Property(a => a.Confidence)
            .IsRequired();

        builder.HasMany(a => a.RelatedEvents)
            .WithMany()
            .UsingEntity(j => j.ToTable("AnomalyResultEntries"));
    }
}
