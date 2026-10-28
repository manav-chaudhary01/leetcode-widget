# LeetCode Desktop Widget

A lightweight, native Windows desktop widget that displays your LeetCode activity heatmap and coding streaks directly on your desktop wallpaper.

Built with C# and WPF on .NET 10, designed to blend naturally into the Windows desktop experience without floating over active applications or stealing user focus.

---

## Overview

Unlike conventional desktop floating windows or heavyweight Electron overlays, the LeetCode Desktop Widget is architected specifically as a wallpaper-layer component. It remains pinned behind active application windows (browsers, IDEs, office software), does not clutter Alt+Tab, and avoids interfering with desktop icons or mouse interactions.

### Key Highlights
- **Wallpaper Integration**: Embedded into the Windows desktop wallpaper layer so it stays strictly behind normal application windows.
- **Minimalist Aesthetics**: Dark translucent GitHub/LeetCode-style heatmap with subtle activity intensity shades.
- **Performance First**: Native WPF rendering with low memory footprint and zero unnecessary idle CPU usage.
- **Privacy Focused**: Uses public LeetCode GraphQL endpoints; no authentication credentials, cookies, or account passwords requested or stored.
- **Offline Resilient**: Local caching ensures data remains visible even during network interruptions.

---

## Technology Stack

- **Target OS**: Windows 11 (x64)
- **Framework**: .NET 10 (C# 14 / WPF)
- **Architecture**: MVVM with clean separation of Desktop Win32 Shell integration, data services, and presentation logic
- **Data Source**: LeetCode Public GraphQL API
- **Testing**: xUnit with FluentAssertions

---

## Project Structure

```
leetcode-widget/
|-- src/
|   |-- LeetCodeDesktopWidget/
|       |-- Desktop/             # Win32 desktop wallpaper hosting & window management
|       |-- Models/              # Data models (Stats, Submissions, Config)
|       |-- Services/            # LeetCode API, local cache, and refresh coordinator
|       |-- ViewModels/          # Presentation logic and bindings
|       |-- Views/               # WPF views, controls, and heatmap rendering
|       |-- Helpers/             # Utility classes and value converters
|       |-- App.xaml
|       |-- MainWindow.xaml
|
|-- tests/
|   |-- LeetCodeDesktopWidget.Tests/ # Unit and integration tests
|
|-- docs/                        # Architectural documentation and design notes
|-- .gitignore
|-- LICENSE
|-- README.md
```

---

## Getting Started

### Prerequisites

- Windows 11 / Windows 10 (x64)
- [.NET 10 SDK](https://dotnet.microsoft.com/)

### Building from Source

```powershell
# Clone repository
git clone https://github.com/username/leetcode-widget.git
cd leetcode-widget

# Restore dependencies
dotnet restore

# Build solution
dotnet build -c Release

# Run tests
dotnet test
```

---

## Roadmap

- [x] **Milestone 0**: Environment setup, project structure, build & test infrastructure.
- [x] **Milestone 1**: Desktop wallpaper layer Win32 integration prototype.
- [x] **Milestone 2**: Minimalist heatmap UI and contribution grid rendering.
- [x] **Milestone 3**: LeetCode GraphQL client and submission calendar parsing.
- [x] **Milestone 4**: Streak calculation and problem-solving statistics.
- [ ] **Milestone 5**: Local caching, periodic background refresh, and idle performance optimization.
- [ ] **Milestone 6**: Widget customization, positioning, and Windows packaging.

---

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.
