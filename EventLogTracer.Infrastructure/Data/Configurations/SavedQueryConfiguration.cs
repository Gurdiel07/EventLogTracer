using EventLogTracer.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EventLogTracer.Infrastructure.Data.Configurations;

public class SavedQueryConfiguration : IEntityTypeConfiguration<SavedQuery>
{
    public void Configure(EntityTypeBuilder<SavedQuery> builder)
    {
        builder.ToTable("SavedQueries");

        builder.HasKey(q => q.Id);

        builder.Property(q => q.Id)
            .ValueGeneratedNever();

        builder.Property(q => q.Name)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(q => q.QueryExpression)
            .IsRequired()
            .HasMaxLength(2048);

        builder.Property(q => q.CreatedAt)
            .IsRequired();

        builder.Property(q => q.LastUsedAt);

        builder.Property(q => q.UseCount)
            .IsRequired();

        builder.HasIndex(q => q.Name);
        builder.HasIndex(q => q.CreatedAt);
    }
}
