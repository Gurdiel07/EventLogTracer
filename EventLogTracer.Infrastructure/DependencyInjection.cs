using System.Net.Http;
using EventLogTracer.Core.Interfaces;
using EventLogTracer.Infrastructure.Data;
using EventLogTracer.Infrastructure.Repositories;
using EventLogTracer.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;


namespace EventLogTracer.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        string connectionString = "Data Source=eventlogtracer.db")
    {
        services.AddDbContext<EventLogTracerDbContext>(options =>
            options.UseSqlite(connectionString));

        services.AddScoped<IEventRepository, EventRepository>();
        services.AddScoped<IAlertService, AlertService>();
        services.AddScoped<IExportService, ExportService>();
        services.AddScoped<IEventCorrelator, EventCorrelator>();

        // Use MockEventLogReader for development; swap for WindowsEventLogReader on Windows
        services.AddSingleton<IEventLogReader, MockEventLogReader>();

        services.AddSingleton<ISearchEngine, SearchEngine>();

        services.AddSingleton<HttpClient>();

        return services;
    }
}
