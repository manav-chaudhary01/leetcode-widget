# LeetCode Desktop Widget

A lightweight, native Windows desktop widget that displays a user's LeetCode submission heatmap, coding streaks, and problem-solving statistics directly on their desktop wallpaper.

Built with C# and WPF on .NET 10, designed to blend naturally into the Windows desktop experience without floating over active application windows, cluttering Alt+Tab, or stealing keyboard/mouse focus.

---

## Key Features

- **Wallpaper-Layer Integration**: Resides behind open application windows (browsers, IDEs, terminals) while remaining visible whenever the desktop is exposed (e.g. Win+D or minimized windows).
- **Draggable & Persistent Positioning**: Freely reposition the widget anywhere on your desktop via deliberate header drag regions; window coordinates are clamped within the virtual screen and persisted across reboots.
- **Account Ownership Verification**: Cryptographic challenge-token verification using public LeetCode profile `aboutMe` field; protects personal desktop widgets from unauthorized username spoofing without requiring passwords or cookies.
- **Strict Account Isolation**: Activity data, streaks, and heatmap grids are strictly gated to the currently verified account; switching accounts immediately resets all metrics.
- **GitHub/LeetCode Contribution Heatmap**: Full 12-month calendar grid with 5 contribution intensity levels, leap year support, weekday and month labels, and interactive hover tooltips.
- **Yearly & All-Time Telemetry**: Solved problems breakdown by difficulty (Easy, Medium, Hard), current and longest streaks derived from daily submissions, active days, and yearly averages.
- **Dynamic Active Years**: Automatically detects and populates active years from the user's LeetCode profile without hardcoded ranges.
- **Persistent Local Configuration**: Automatically remembers username, verified state, selected year, and window coordinates across restarts in `%LOCALAPPDATA%\LeetCodeDesktopWidget\config.json`.
- **Instant Per-Account Offline Cache**: Persists activity to `%LOCALAPPDATA%\LeetCodeDesktopWidget\cache_<username>.json`, displaying cached data and freshness indicators immediately upon launch without network lag.
- **Background Synchronization**: 60-minute automatic periodic background refresh with strict concurrency coalescing (no duplicate requests).
- **Windows Shell Resilience**: Survives Explorer restarts (`explorer.exe`), display configuration changes, multi-monitor shifts, sleep/wake cycles, and session lock/unlock events.
- **Privacy First**: Uses LeetCode's public GraphQL endpoint. Requires only a public username; never requests or persists passwords, tokens, or session cookies.

---

## System Requirements

- **Operating System**: Windows 11 or Windows 10 (version 1903 or later, x64)
- **Runtime / SDK**: [.NET 10 LTS Runtime or SDK](https://dotnet.microsoft.com/download/dotnet/10.0)

---

## Building and Running

### Build from Source

```powershell
# Clone the repository
git clone https://github.com/manav-chaudhary01/leetcode-widget.git
cd leetcode-widget

# Build in Release configuration
dotnet build -c Release

# Run automated tests
dotnet test -c Release --logger "console;verbosity=detailed"

# Launch the widget
dotnet run --project src/LeetCodeDesktopWidget -c Release
```

---

## User Guide

### 1. Connecting Your Account & Ownership Verification
1. On initial launch, the widget displays the onboarding surface. Enter your public LeetCode username and click **Verify Ownership**.
2. A temporary cryptographic challenge token is generated (e.g., `lc-verify-xxxxxxxxxxxx`).
3. Click **📋 Copy** to copy the token.
4. Click **↗ Open LeetCode Profile** to open your profile bio settings in your default browser.
5. Paste the token into your LeetCode profile **About Me** field and click **Save**.
6. Return to the widget and click **Verify Ownership**.
7. Once verified, the verification overlay closes, your live activity and heatmap are loaded, and the verified status badge appears. You can safely remove the token from your profile bio.

### 2. Moving the Widget & Persistent Position
- Click and drag anywhere along the top brand region (`● LEETCODE ⋮⋮`) to move the widget.
- The widget uses a minimum drag distance threshold to prevent accidental clicks from moving the window.
- The window automatically clamps within the bounds of your multi-monitor virtual screen.
- Coordinates (`WindowX`, `WindowY`) are automatically saved to `%LOCALAPPDATA%\LeetCodeDesktopWidget\config.json` and restored across restarts.

### 3. Year Selector
- Use the dropdown in the hero section to switch between your account's active submission years. Active years are queried dynamically from your profile history.

### 4. Manual Refresh
- Click the **↻** button to manually synchronize with LeetCode. If a sync is already running, subsequent clicks coalesce into the active request without redundant HTTP calls.

---

## How It Works

### Desktop Wallpaper Integration
The widget interacts with the Windows Shell to anchor itself as an unobtrusive background component:
1. **Desktop Layer Ownership**: It discovers the Windows desktop host window (`Progman`) and establishes ownership (`GWLP_HWNDPARENT`) while maintaining a top-level `WS_POPUP` window. This preserves native DirectComposition/DWM layered transparency, ensures full mouse interactivity within the widget, and keeps desktop icons outside the widget completely interactive.
2. **Bottom Z-Order Pinning**: The window is pinned to `HWND_BOTTOM` and intercepts `WM_WINDOWPOSCHANGING` in its subclassed window procedure, ensuring it reliably stays behind open applications (Chrome, VS Code, terminals) and remains visible when the desktop is exposed via Win+D ("Show Desktop").
3. **No Focus / No Alt+Tab**: Extended window styles `WS_EX_TOOLWINDOW` and `WS_EX_NOACTIVATE` guarantee that the widget never steals input focus or appears in Alt+Tab window switching.
4. **Shell Reconnection**: Intercepts the registered `TaskbarCreated` Win32 message. If `explorer.exe` restarts, the widget automatically rediscovers the desktop host and re-anchors itself.
5. **Multi-Monitor & DPI Clamping**: Listens for `WM_DISPLAYCHANGE`, `WM_SETTINGCHANGE`, and `SystemEvents.DisplaySettingsChanged` to clamp window coordinates safely inside the virtual screen bounds across all active displays.

### Data Synchronization & Caching
- **Endpoint**: Communicates via HTTPS with LeetCode's public GraphQL endpoint (`https://leetcode.com/graphql`).
- **Cache Persistence**: Successful responses are serialized atomically into `%LOCALAPPDATA%\LeetCodeDesktopWidget\cache_<username>.json` using temporary file replacement (`.tmp` + `File.Move`).
- **Account Isolation**: Data and statistics are strictly isolated by verified username. If you switch to another account, cached data from previous accounts is immediately cleared.
- **Offline Mode**: If offline or if LeetCode encounters network errors, the widget retains previously cached submissions for the current verified account and updates the status indicator to `"Verified · Offline · cached [time]"`.
- **Concurrency Protection**: The `RefreshCoordinator` uses non-blocking atomic locks to guarantee that duplicate periodic triggers are discarded and concurrent manual triggers coalesce with in-flight HTTP requests.

---

## Privacy & Security

- **No Secrets**: No authentication tokens, cookies, or account passwords are required, requested, or stored.
- **Sanitized Input**: Usernames are strictly validated (alphanumeric, underscores, hyphens, maximum 40 characters) and stripped of any leading `@` symbols.
- **HTTPS Only**: All network traffic is sent over secure TLS connections directly to LeetCode's public endpoint.

---

## Known Limitations

- **Unofficial GraphQL Endpoint**: The widget relies on LeetCode's public web GraphQL schema. If LeetCode makes breaking changes to their public schema, an update to the GraphQL query models may be required.
- **Rate Limiting**: Sending too many rapid manual refresh requests may trigger temporary HTTP 429 rate limiting from LeetCode. The widget handles rate limiting gracefully by displaying a warning and backing off.
- **Visual Design Status**: The current UI represents a functional release candidate composition; visual refinements and layout polishing will continue in future updates.

---

## Troubleshooting

| Issue | Cause | Solution |
| :--- | :--- | :--- |
| **Status shows "User not found"** | Username mistyped or account is private | Verify your username on [leetcode.com](https://leetcode.com). Click ✎ to re-enter. |
| **Verification fails** | Token missing from LeetCode bio or challenge expired | Ensure the challenge token is saved in your LeetCode profile 'About Me' field and click 'Verify Ownership' within 15 minutes. |
| **Status shows "Offline · cached ..."** | Internet disconnected or LeetCode unreachable | Check your network connection. Cached data remains displayed. Click ↻ when back online. |
| **Widget moved after resolution change** | Display resolution reduced | The widget automatically clamps to the visible desktop. Drag to reposition if desired. |
| **Explorer restarted and widget is hidden** | Shell re-initialization lag | The widget catches `TaskbarCreated` and re-attaches automatically within 500ms. |

---

## Project Structure

```
leetcode-widget/
|-- src/
|   |-- LeetCodeDesktopWidget/
|       |-- Desktop/             # Win32 desktop wallpaper hosting & window management
|       |-- Models/              # Domain models, config records, and cached schemas
|       |-- Services/            # LeetCode GraphQL client, cache service, and refresh coordinator
|       |-- ViewModels/          # MVVM presentation logic
|       |-- Views/               # WPF views, controls, and heatmap canvas rendering
|       |-- Helpers/             # Value converters and date/time formatters
|       |-- App.xaml
|       |-- MainWindow.xaml
|
|-- tests/
|   |-- LeetCodeDesktopWidget.Tests/ # xUnit test suite (194 automated tests)
|
|-- README.md
|-- LICENSE
```

---

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.
