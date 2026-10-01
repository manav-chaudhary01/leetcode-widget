# Windows Desktop Layer Integration

## 1. Windows Desktop Window Hierarchy Investigation

On modern Windows (specifically Windows 11 and Windows 10), the desktop is not a single static canvas but a layered hierarchy managed by `explorer.exe` and composed by the Desktop Window Manager (DWM):

```
Top-Level Window Hierarchy (Z-Order, front-to-back):
  [Normal Applications: Chrome, VS Code, Terminal]  <-- Highest Z-Order
  [Taskbar: Shell_TrayWnd]
  ...
  [Desktop Icon View: SHELLDLL_DefView / FolderView]
  [Wallpaper Canvas: WorkerW / Progman]             <-- Lowest Z-Order
```

### Key Window Classes & Messages:
1. **Progman ("Program Manager")**:
   - The root shell window created by `explorer.exe`.
   - In standard configurations, it holds `SHELLDLL_DefView` (which contains desktop icons).
2. **WorkerW**:
   - When message `0x052C` (`WM_SPAWN_WORKER`) is sent to `Progman` via `SendMessageTimeout`, Explorer splits the desktop wallpaper rendering from the desktop icons.
   - Depending on Windows 10/11 version and wallpaper configuration (slideshow, active theme), Explorer either places `SHELLDLL_DefView` inside a `WorkerW` and creates a sibling `WorkerW` behind it, or renders the wallpaper directly behind `Progman`.
3. **Window Station & Desktop Isolation**:
   - Windows divides sessions into Window Stations (`WinSta0`) and desktops (`Default`, `Winlogon`, `Screen-Saver`).
   - The interactive desktop hosting Explorer is `Default`. If an application is launched in a named sandbox or test runner desktop, attempts to find `Progman` must account for desktop handle boundaries.

---

## 2. Desktop-Layer Hosting Implementation

Our architecture uses a two-tier strategy in `DesktopHost.cs` and `DesktopWindowManager.cs`:

### Primary Mode: Desktop Parent (`WorkerW` / `Progman`)
- When `DiscoverDesktopHost()` detects the active `WorkerW` or `Progman`:
  1. The window styles are modified from `WS_POPUP` to `WS_CHILD | WS_CLIPSIBLINGS`.
  2. `SetParent(hwnd, targetHandle)` embeds the widget into the desktop hierarchy.
  3. Because the widget is an embedded child of the desktop wallpaper layer:
     - It naturally remains behind all normal top-level application windows.
     - Pressing `Win+D` ("Show Desktop") does not minimize the widget because Windows only minimizes top-level unowned windows.
     - It never appears in the `Alt+Tab` application switcher.

### Secondary Mode: `BottomPinnedFallback`
- If running under restricted environments or if `SetParent` cannot be completed:
  1. The window is configured as a borderless window with `WS_EX_TOOLWINDOW` and `WS_EX_NOACTIVATE`.
  2. Subclassed `WndProc` intercepts `WM_WINDOWPOSCHANGING`:
     ```csharp
     pWinPos->hwndInsertAfter = DesktopInterop.HWND_BOTTOM;
     ```
  3. `SetWindowPos` keeps the window pinned at `HWND_BOTTOM` behind other normal windows.

---

## 3. Shell Event & Recovery Handling

1. **Explorer Restart Recovery**:
   - Registers the Windows broadcast message `"TaskbarCreated"` via `RegisterWindowMessage("TaskbarCreated")`.
   - When Explorer crashes or restarts, this message is broadcast. `DesktopWindowManager` catches it, executes `DiscoverDesktopHost()`, and re-attaches the widget to the new desktop hierarchy.
2. **Display & DPI Changes**:
   - Intercepts `WM_DISPLAYCHANGE` and `WM_SETTINGCHANGE` to reposition or refresh the window layout.
3. **Focus & Alt+Tab Preservation**:
   - `WS_EX_TOOLWINDOW` (`0x00000080`): Excludes the window from the Windows task switcher (Alt+Tab).
   - `WS_EX_NOACTIVATE` (`0x08000000`): Clicking on the widget (e.g. year selectors, hover tooltips) does not take focus away from active applications.

---

## 4. Known Limitations & Edge Cases

1. **Third-Party Live Wallpaper Utilities**:
   - Tools like Wallpaper Engine or Lively Wallpaper also hook into `0x052C` and `WorkerW`. If another utility forcefully re-attaches `WorkerW`, `DesktopWindowManager` will detect the `"TaskbarCreated"` or window position update and re-sync.
2. **Virtual Desktops (Win+Tab)**:
   - Child windows parented to `Progman`/`WorkerW` are shared across virtual desktops because the wallpaper layer is shared. This provides a unified widget experience across desktops.
