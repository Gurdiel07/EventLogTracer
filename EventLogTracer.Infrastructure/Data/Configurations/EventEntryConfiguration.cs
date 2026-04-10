using EventLogTracer.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EventLogTracer.Infrastructure.Data.Configurations;

public class EventEntryConfiguration : IEntityTypeConfiguration<EventEntry>
{
    public void Configure(EntityTypeBuilder<EventEntry> builder)
    {
        builder.ToTable("EventEntries");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .ValueGeneratedNever();

        builder.Property(e => e.EventId)
            .IsRequired();

        builder.Property(e => e.Level)
            .IsRequired()
            .HasConversion<string>();

        builder.Property(e => e.Source)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(e => e.LogName)
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(e => e.TimeCreated)
            .IsRequired();

        builder.Property(e => e.MachineName)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(e => e.Message)
            .IsRequired();

        builder.Property(e => e.Category)
            .HasMaxLength(256);

        builder.Property(e => e.UserId)
            .HasMaxLength(256);

        builder.Property(e => e.Keywords)
            .HasMaxLength(512);

        builder.Property(e => e.BookmarkColor)
            .HasMaxLength(32);

        builder.Property(e => e.BookmarkComment)
            .HasMaxLength(1024);

        // Store Tags as a JSON-serialized string in SQLite
        builder.Property(e => e.Tags)
            .HasConversion(
                v => string.Join(',', v),
                v => v.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList())
            .HasMaxLength(2048);

        builder.HasIndex(e => e.TimeCreated);
        builder.HasIndex(e => e.Level);
        builder.HasIndex(e => e.Source);
        builder.HasIndex(e => e.LogName);
        builder.HasIndex(e => e.IsBookmarked);
    }
}
