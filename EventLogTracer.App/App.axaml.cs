using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using EventLogTracer.App.Services;
using EventLogTracer.App.ViewModels;
using EventLogTracer.App.Views;
using EventLogTracer.Core.Interfaces;
using EventLogTracer.Infrastructure;
using EventLogTracer.ML.Services;
using EventLogTracer.Infrastructure.Data;
using Microsoft.Extensions.DependencyInjection;

namespace EventLogTracer.App;

public partial class App : Application
{
    private IServiceProvider? _services;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        _services = BuildServices();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var mainWindow = new MainWindow
            {
                DataContext = _services.GetRequiredService<MainWindowViewModel>()
            };

            // Wire up desktop notifications to the main window reference
            _services.GetRequiredService<DesktopNotifier>().AttachToWindow(mainWindow);

            desktop.MainWindow = mainWindow;
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static IServiceProvider BuildServices()
    {
        var services = new ServiceCollection();

        // Infrastructure (EF Core + SQLite, Mock reader, repositories, services)
        services.AddInfrastructure();

        // Desktop notifier — singleton, concrete type needed for AttachToWindow
        services.AddSingleton<DesktopNotifier>();
        services.AddSingleton<IDesktopNotifier>(sp => sp.GetRequiredService<DesktopNotifier>());

        // ML anomaly detector — singleton (trained model lives for app lifetime)
        services.AddSingleton<IAnomalyDetector, AnomalyDetector>();

        // ViewModels
        services.AddTransient<DashboardViewModel>();
        services.AddTransient<EventViewerViewModel>();
        services.AddTransient<TimelineViewModel>();
        services.AddTransient<AlertsViewModel>();
        services.AddTransient<SearchViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<MainWindowViewModel>();

        var provider = services.BuildServiceProvider();

        // Ensure the SQLite database and all tables exist before the UI starts
        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EventLogTracerDbContext>();
            db.Database.EnsureCreated();
        }

        return provider;
    }
}
