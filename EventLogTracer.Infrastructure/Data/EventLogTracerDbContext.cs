using EventLogTracer.Core.Models;
using EventLogTracer.Infrastructure.Data.Configurations;
using Microsoft.EntityFrameworkCore;

namespace EventLogTracer.Infrastructure.Data;

public class EventLogTracerDbContext : DbContext
{
    public DbSet<EventEntry> EventEntries => Set<EventEntry>();
    public DbSet<AlertRule> AlertRules => Set<AlertRule>();
    public DbSet<EventCorrelation> EventCorrelations => Set<EventCorrelation>();
    public DbSet<AnomalyResult> AnomalyResults => Set<AnomalyResult>();
    public DbSet<SavedQuery> SavedQueries => Set<SavedQuery>();

    public EventLogTracerDbContext(DbContextOptions<EventLogTracerDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfiguration(new EventEntryConfiguration());
        modelBuilder.ApplyConfiguration(new AlertRuleConfiguration());
        modelBuilder.ApplyConfiguration(new EventCorrelationConfiguration());
        modelBuilder.ApplyConfiguration(new AnomalyResultConfiguration());
        modelBuilder.ApplyConfiguration(new SavedQueryConfiguration());
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.IsConfigured)
        {
            optionsBuilder.UseSqlite("Data Source=eventlogtracer.db");
        }
    }
}
