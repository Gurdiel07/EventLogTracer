using EventLogTracer.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EventLogTracer.Infrastructure.Data.Configurations;

public class AlertRuleConfiguration : IEntityTypeConfiguration<AlertRule>
{
    public void Configure(EntityTypeBuilder<AlertRule> builder)
    {
        builder.ToTable("AlertRules");

        builder.HasKey(r => r.Id);

        builder.Property(r => r.Id)
            .ValueGeneratedNever();

        builder.Property(r => r.Name)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(r => r.IsEnabled)
            .IsRequired();

        builder.Property(r => r.NotificationType)
            .IsRequired()
            .HasConversion<string>();

        builder.Property(r => r.NotificationTarget)
            .IsRequired()
            .HasMaxLength(512);

        builder.Property(r => r.CreatedAt)
            .IsRequired();

        // Owned entity: EventFilter stored as JSON-like owned columns
        builder.OwnsOne(r => r.Filter, filter =>
        {
            filter.Property(f => f.SearchText).HasMaxLength(1024);
            filter.Property(f => f.IsRegex).IsRequired();
            filter.Property(f => f.StartDate);
            filter.Property(f => f.EndDate);

            filter.Property(f => f.EventIds)
                .HasConversion(
                    v => v == null ? null : string.Join(',', v),
                    v => v == null ? null : v.Split(',', StringSplitOptions.RemoveEmptyEntries)
                            .Select(int.Parse).ToList());

            filter.Property(f => f.Levels)
                .HasConversion(
                    v => v == null ? null : string.Join(',', v.Select(l => l.ToString())),
                    v => v == null ? null : v.Split(',', StringSplitOptions.RemoveEmptyEntries)
                            .Select(s => Enum.Parse<EventLogTracer.Core.Enums.EventLevel>(s)).ToList());

            filter.Property(f => f.Sources)
                .HasConversion(
                    v => v == null ? null : string.Join('|', v),
                    v => v == null ? null : v.Split('|', StringSplitOptions.RemoveEmptyEntries).ToList());

            filter.Property(f => f.LogNames)
                .HasConversion(
                    v => v == null ? null : string.Join('|', v),
                    v => v == null ? null : v.Split('|', StringSplitOptions.RemoveEmptyEntries).ToList());

            filter.Property(f => f.Tags)
                .HasConversion(
                    v => v == null ? null : string.Join('|', v),
                    v => v == null ? null : v.Split('|', StringSplitOptions.RemoveEmptyEntries).ToList());
        });
    }
}
