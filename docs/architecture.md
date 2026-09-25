# Architecture & Technical Design

## 1. System Overview

`LeetCodeDesktopWidget` is a native Windows desktop widget engineered to display a user's LeetCode submission activity directly on the desktop wallpaper layer.

### Key Architectural Principles

1. **True Desktop Wallpaper Integration**: The widget must be hosted behind all normal application windows and must not interfere with daily desktop usage (window management, Alt+Tab, desktop icons, etc.).
2. **Minimal Resource Footprint**: The application should idle at near-zero CPU and low memory (<50MB). Rendering occurs only when data or display configuration changes.
3. **Decoupled Architecture**: Separation of concerns between Win32 Shell integration, data fetching, local caching, and UI presentation.
4. **Resilience & Privacy**: Zero credential requirements (public GraphQL queries only), graceful degradation on network outage, and persistent local caching.

---

## 2. Component Layers

```
+------------------------------------------------------------------+
|                     Presentation Layer (WPF)                     |
|  - MainWindow (Window styling, transparency, hit-testing)        |
|  - HeatmapCanvas / DrawingVisual (Efficient grid rendering)      |
|  - MainViewModel (Observable properties, user interaction)       |
+------------------------------------------------------------------+
                                 |
                                 v
+------------------------------------------------------------------+
|                   Application & Service Layer                    |
|  - LeetCodeService (GraphQL queries, DTO mapping)                |
|  - CacheService (Local JSON storage & schema validation)         |
|  - RefreshCoordinator (Periodic timer, backoff, triggers)        |
+------------------------------------------------------------------+
                                 |
                                 v
+------------------------------------------------------------------+
|                   Desktop Integration Layer                      |
|  - DesktopHost (Progman/WorkerW discovery, window parenting)     |
|  - WindowManager (Styles: WS_EX_TOOLWINDOW, WS_EX_NOACTIVATE)   |
|  - DisplayMonitor (Resolution changes, multi-monitor bounds)     |
+------------------------------------------------------------------+
```

---

## 3. Technology Stack & Environment

- **Target Framework**: .NET 10 (LTS), `net10.0-windows`
- **UI Framework**: Windows Presentation Foundation (WPF)
- **Language**: C# 14
- **Testing**: xUnit with .NET Test SDK
