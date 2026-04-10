# Event Log Tracer

A cross-platform desktop application for analyzing and monitoring **Windows Event Logs** in real-time.  
Built with **.NET 8**, **Avalonia UI**, and **Clean Architecture**.

---

## Features

- Real-time event monitoring (via Windows Event Log API or Mock reader for dev)
- Advanced filtering: by level, source, log name, event ID, date range, text/regex
- Bookmarks, tags, and comments on events
- Alert rules with Desktop / Email / Webhook notifications
- Event correlation (time-burst analysis)
- Export to CSV, JSON, XML
- ML.NET anomaly detection (scaffold ready, training pipeline TBD)
- Dark-themed Avalonia UI, cross-platform (macOS / Windows / Linux)

---

## Solution Structure

```
EventLogTracer.sln
├── EventLogTracer.App          — Avalonia UI (Views, ViewModels, DI bootstrap)
├── EventLogTracer.Core         — Domain models, interfaces, enums
├── EventLogTracer.Infrastructure — EF Core/SQLite, MockEventLogReader, repositories, services
├── EventLogTracer.ML           — ML.NET anomaly detection scaffold
└── EventLogTracer.Tests        — xUnit tests (FluentAssertions + Moq)
```

---

## Prerequisites

| Tool | Version |
|------|---------|
| [.NET SDK](https://dotnet.microsoft.com/download) | 8.0 or later |
| Avalonia templates (optional) | `dotnet new install Avalonia.Templates` |

---

## Build & Run

```bash
# Clone / open the workspace
cd "Event Log Tracer"

# Restore all NuGet packages
dotnet restore EventLogTracer.sln

# Build (Debug)
dotnet build EventLogTracer.sln

# Run the desktop app
dotnet run --project EventLogTracer.App/EventLogTracer.App.csproj

# Run tests
dotnet test EventLogTracer.Tests/EventLogTracer.Tests.csproj --verbosity normal
```

---

## Database Migrations

The app uses **EF Core + SQLite**. To apply the initial migration:

```bash
# Install EF tools (once)
dotnet tool install --global dotnet-ef

# Create initial migration
dotnet ef migrations add InitialCreate \
    --project EventLogTracer.Infrastructure \
    --startup-project EventLogTracer.App

# Apply migration (creates eventlogtracer.db)
dotnet ef database update \
    --project EventLogTracer.Infrastructure \
    --startup-project EventLogTracer.App
```

---

## Development Notes

- **MockEventLogReader** (in `EventLogTracer.Infrastructure/Services/`) generates realistic fake events every 1–3 seconds. Toggle it in Settings → "Use Mock Event Reader".
- On **Windows**, swap `MockEventLogReader` with a `WindowsEventLogReader` implementation (not yet implemented) that calls `System.Diagnostics.Eventing.Reader`.
- **ML.NET** packages are included. The `AnomalyDetector` class in `EventLogTracer.ML` is a scaffold — implement the SrCnn / IID Spike Detection pipeline there.

---

## Tech Stack

| Layer | Technology |
|-------|-----------|
| UI | Avalonia UI 11.x + FluentTheme (Dark) |
| MVVM | CommunityToolkit.Mvvm 8.x |
| ORM | Entity Framework Core 8 + SQLite |
| Charts | LiveChartsCore.SkiaSharpView.Avalonia |
| ML | ML.NET 3.x |
| Logging | Serilog (Console + rolling File sinks) |
| Tests | xUnit + FluentAssertions + Moq |
| DI | Microsoft.Extensions.DependencyInjection |

---

## License

MIT
