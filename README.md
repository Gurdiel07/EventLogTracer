# ⚡ Event Log Tracer

**Real-time Windows Event Log analysis and monitoring application**

![.NET](https://img.shields.io/badge/.NET-8.0-purple) ![Avalonia](https://img.shields.io/badge/Avalonia-11-blue) ![ML.NET](https://img.shields.io/badge/ML.NET-3.0-orange) ![License](https://img.shields.io/badge/license-MIT-green)

---

## Screenshots

> Screenshots coming soon.

---

## Features

- 📊 **Real-time event monitoring** with live data feed and event rate display
- 🔍 **Advanced search** with regex, boolean operators (AND/OR/NOT), field filters (`level:Error`, `source:Security`, `eventid:4625`), and saved queries
- 📈 **Interactive dashboard** with pie, line, and bar charts powered by LiveCharts2
- 📅 **Timeline visualization** with zoom, navigation, and level-based scatter lanes
- 🔔 **Alert system** with desktop notifications, email placeholders, and webhook (HTTP POST) support
- 🤖 **ML.NET anomaly detection** — frequency spike detection (IID), error-rate analysis, and unknown source detection
- 🔗 **Event correlation engine** — burst detection, error cascades, authentication sequences, and service lifecycle grouping
- 💾 **Export** to CSV (UTF-8 BOM, RFC 4180), JSON (indented, enum-as-string), and XML
- 🏷️ **Tag management** and event bookmarking with comments
- ⌨️ **Keyboard shortcuts** for power users
- 🌙 **Dark theme** UI throughout

---

## Quick Start

### Option A: Download & Run *(no SDK required)*

1. Go to [Releases](../../releases)
2. Download `EventLogTracer-win-x64.zip`
3. Extract the archive and run `EventLogTracer.App.exe`

### Option B: Build from Source

**Prerequisites:** [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

```bash
git clone https://github.com/YOUR_USER/EventLogTracer.git
cd EventLogTracer
dotnet restore EventLogTracer.sln
dotnet run --project EventLogTracer.App
```

---

## Usage

1. **Start Monitoring** — click the *Start Monitoring* button in the status bar (or press `Ctrl+M`) to begin receiving live events from the mock data source.
2. **Navigate views** — use `Ctrl+1` through `Ctrl+6` or the left sidebar.
3. **Search** — switch to the *Search* view and write queries like `level:Error AND source:"Security"` or `/failed.*logon/`.
4. **Configure alerts** — open the *Alerts* view, click *+ New Rule*, and choose Desktop, Email, or Webhook delivery.
5. **Export data** — go to *Settings → Export Events*, pick a format and date range, then click *Export*.

### Keyboard Shortcuts

| Shortcut | Action |
|----------|--------|
| `Ctrl+1` … `Ctrl+6` | Navigate to Dashboard / Event Viewer / Timeline / Alerts / Search / Settings |
| `Ctrl+M` | Toggle monitoring on / off |
| `Ctrl+F` | Focus the search / filter box in the current view |
| `Ctrl+E` | Quick-export visible Event Viewer events to CSV |
| `F5` | Refresh the current page |
| `Esc` | Clear selection or close edit panels |

### Search Syntax

| Syntax | Example | Description |
|--------|---------|-------------|
| Simple text | `svchost` | Matches `Source` or `Message` |
| Regex | `/failed.*logon/` | Regex applied to `Message` |
| Field filter | `level:Error` | Exact field match |
| Boolean | `level:Error AND source:Security` | Logical operators |
| Phrase | `"access denied"` | Exact phrase |
| Grouping | `(level:Critical OR level:Error) AND NOT source:WMI` | Precedence via parens |

Available field names: `level`, `source`, `eventid`, `log`, `machine`

---

## Architecture

```
EventLogTracer.sln
├── EventLogTracer.Core            # Domain models, interfaces, enums
├── EventLogTracer.Infrastructure  # EF Core + SQLite, repositories, services
├── EventLogTracer.ML              # ML.NET anomaly detection
├── EventLogTracer.App             # Avalonia UI — views, view-models, converters
└── EventLogTracer.Tests           # xUnit unit tests
```

The project follows **Clean Architecture**: `Core` has zero dependencies; `Infrastructure` and `ML` depend only on `Core`; `App` depends on all layers and wires DI.

---

## Tech Stack

| Layer | Technology |
|-------|-----------|
| Language / Runtime | C# 12 · .NET 8 |
| UI Framework | [Avalonia UI 11](https://avaloniaui.net/) |
| MVVM | [CommunityToolkit.Mvvm](https://learn.microsoft.com/dotnet/communitytoolkit/mvvm/) |
| Database | Entity Framework Core 8 + SQLite |
| Charts | [LiveCharts2](https://livecharts.dev/) (SkiaSharp) |
| Machine Learning | [ML.NET 3](https://dotnet.microsoft.com/apps/machinelearning-ai/ml-dotnet) (TimeSeries) |
| Logging | [Serilog](https://serilog.net/) |

---

## License

This project is licensed under the **MIT License** — see the [LICENSE](LICENSE) file for details.
