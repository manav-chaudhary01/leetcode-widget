# LeetCode Desktop Widget

A lightweight, native Windows desktop widget that displays a user's LeetCode submission heatmap, coding streaks, and problem-solving statistics directly on their desktop wallpaper.

Built with C# and WPF on .NET 10, designed to blend naturally into the Windows desktop experience without floating over active application windows, cluttering Alt+Tab, or stealing keyboard/mouse focus.

---

## Key Features

- **Wallpaper-Layer Integration**: Resides behind open application windows (browsers, IDEs, terminals) while remaining visible whenever the desktop is exposed (e.g. Win+D or minimized windows).
- **GitHub/LeetCode Contribution Heatmap**: Complete 53-week calendar grid with 5 contribution intensity levels, leap year support, weekday and month labels, and hover tooltips.
- **Yearly & All-Time Telemetry**: Solved problems breakdown by difficulty (Easy, Medium, Hard), current and longest streaks derived from submission history, active days, and yearly averages.
- **Persistent Local Configuration**: Automatically remembers username, selected year, and refresh settings across restarts in `%LOCALAPPDATA%\LeetCodeDesktopWidget\config.json`.
- **Instant Offline Cache**: Persists activity to `%LOCALAPPDATA%\LeetCodeDesktopWidget\cache.json`, displaying cached data and freshness indicators immediately upon launch without network lag.
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

### 1. Configuring Your Username
1. On initial launch, click the `@configure-user` link in the top-left corner (or the ✎ pencil button).
2. Type your public LeetCode username.
3. Press **Enter** or click the **✓** button to save. (Press **Esc** or click **✕** to cancel).
4. The widget will sanitize the username, persist your choice, and initiate a background sync with LeetCode.

### 2. Year Selector
- Use the dropdown in the header to switch between active years. The heatmap and the statistics strip update instantly to reflect the selected calendar year.

### 3. Manual Refresh
- Click the **↻** button to trigger a manual synchronization. If a background sync is already in flight, the manual request coalesces with it to prevent duplicate network calls.

---

## How It Works

### Desktop Wallpaper Integration
The widget interacts with the Windows Shell to anchor itself as an unobtrusive background component:
1. **WorkerW Hosting**: It discovers the Windows desktop wallpaper host window (`WorkerW`, spawned by `Progman` via message `0x052C`) and parents the widget directly behind the desktop icon view (`SHELLDLL_DefView`).
2. **Bottom-Pinned Fallback**: If running in an environment without WorkerW (e.g. specialized sandbox or secondary desktop station), it switches to a bottom-pinned popup window (`HWND_BOTTOM`) while intercepting `WM_WINDOWPOSCHANGING` to prevent rising above applications.
3. **No Focus / No Alt+Tab**: Extended window styles `WS_EX_TOOLWINDOW` and `WS_EX_NOACTIVATE` guarantee that the widget never steals input focus or appears in Alt+Tab window switching.
4. **Shell Reconnection**: Intercepts the registered `TaskbarCreated` Win32 message. If `explorer.exe` restarts, the widget automatically rediscovers the desktop host and re-anchors itself.
5. **Display & Work Area Clamping**: Listens for `WM_DISPLAYCHANGE`, `WM_SETTINGCHANGE`, and `SystemEvents.DisplaySettingsChanged` to clamp window coordinates safely inside the visible primary monitor work area.

### Data Synchronization & Caching
- **Endpoint**: Communicates via HTTPS with LeetCode's public GraphQL endpoint (`https://leetcode.com/graphql`).
- **Cache Persistence**: Successful responses are serialized atomically into `%LOCALAPPDATA%\LeetCodeDesktopWidget\cache.json` using temporary file replacement (`.tmp` + `File.Move`).
- **Offline Mode**: If offline or if LeetCode encounters network errors, the widget retains previously cached submissions and updates the status indicator to `"Offline · cached [time]"`.
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
- **Multi-Monitor Targeting**: The widget anchors to the primary desktop display. Moving the widget across arbitrary secondary monitors is not currently automated.

---

## Troubleshooting

| Issue | Cause | Solution |
| :--- | :--- | :--- |
| **Status shows "User not found"** | Username mistyped or account is private | Verify your username on [leetcode.com](https://leetcode.com). Click ✎ to re-enter. |
| **Status shows "Offline · cached ..."** | Internet disconnected or LeetCode unreachable | Check your network connection. Cached data remains displayed. Click ↻ when back online. |
| **Widget moved after resolution change** | Display resolution reduced | The widget automatically clamps to the work area. If needed, restart the application to reposition. |
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
|   |-- LeetCodeDesktopWidget.Tests/ # xUnit test suite (86 automated tests)
|
|-- README.md
|-- LICENSE
```

---

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.
