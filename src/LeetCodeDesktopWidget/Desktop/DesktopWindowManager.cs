using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;

namespace LeetCodeDesktopWidget.Desktop;

/// <summary>
/// Manages window styles, Z-order positioning, Win32 message hooks, and lifecycle events.
/// Hardened for Explorer restarts, sleep/wake cycles, display changes, and session unlock.
/// </summary>
public class DesktopWindowManager : IDisposable
{
    private readonly Window _window;
    private IntPtr _hwnd = IntPtr.Zero;
    private HwndSource? _hwndSource;
    private bool _hookAdded;
    private readonly uint _taskbarCreatedMsg;
    private DesktopHostInfo _hostInfo = new();
    private bool _isDisposed;

    public DesktopHostInfo CurrentHostInfo => _hostInfo;
    public event Action? ExplorerRestarted;
    public event Action? DisplayChanged;

    public DesktopWindowManager(Window window)
    {
        _window = window ?? throw new ArgumentNullException(nameof(window));
        _taskbarCreatedMsg = DesktopInterop.RegisterWindowMessage("TaskbarCreated");

        // Subscribe to system lifecycle events
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        SystemEvents.SessionSwitch += OnSessionSwitch;
    }

    /// <summary>
    /// Attaches the target WPF window to the Windows desktop wallpaper layer.
    /// </summary>
    public bool AttachToDesktop()
    {
        if (_isDisposed) return false;

        _hwnd = new WindowInteropHelper(_window).EnsureHandle();

        if (_hwndSource == null)
        {
            _hwndSource = HwndSource.FromHwnd(_hwnd);
        }

        if (!_hookAdded && _hwndSource != null)
        {
            _hwndSource.AddHook(WndProc);
            _hookAdded = true;
        }

        // 1. Configure extended styles: ToolWindow (no Alt+Tab) and NoActivate (no focus stealing)
        long exStyle = DesktopInterop.GetWindowLong(_hwnd, DesktopInterop.GWL_EXSTYLE);
        exStyle |= DesktopInterop.WS_EX_TOOLWINDOW;
        exStyle |= DesktopInterop.WS_EX_NOACTIVATE;
        DesktopInterop.SetWindowLong(_hwnd, DesktopInterop.GWL_EXSTYLE, exStyle);

        // 2. Discover Desktop Host (WorkerW or Progman)
        _hostInfo = DesktopHost.DiscoverDesktopHost();

        if (_hostInfo.TargetParentHandle != IntPtr.Zero &&
            (_hostInfo.HostType == DesktopHostType.WorkerW || _hostInfo.HostType == DesktopHostType.Progman))
        {
            // Set as child of the desktop host
            long style = DesktopInterop.GetWindowLong(_hwnd, DesktopInterop.GWL_STYLE);
            style = (style & ~DesktopInterop.WS_POPUP) | DesktopInterop.WS_CHILD | DesktopInterop.WS_CLIPSIBLINGS;
            DesktopInterop.SetWindowLong(_hwnd, DesktopInterop.GWL_STYLE, style);

            DesktopInterop.SetParent(_hwnd, _hostInfo.TargetParentHandle);
            _hostInfo.IsAttached = true;
        }
        else
        {
            // Fallback: If previous parent was invalidated, restore top-level popup style
            long style = DesktopInterop.GetWindowLong(_hwnd, DesktopInterop.GWL_STYLE);
            style = (style & ~DesktopInterop.WS_CHILD) | DesktopInterop.WS_POPUP;
            DesktopInterop.SetWindowLong(_hwnd, DesktopInterop.GWL_STYLE, style);
            DesktopInterop.SetParent(_hwnd, IntPtr.Zero);

            SendToBottom();
            _hostInfo.IsAttached = false;
        }

        // Apply updated frame and refresh Z-order
        DesktopInterop.SetWindowPos(
            _hwnd,
            _hostInfo.IsAttached ? IntPtr.Zero : DesktopInterop.HWND_BOTTOM,
            0, 0, 0, 0,
            DesktopInterop.SWP_NOMOVE | DesktopInterop.SWP_NOSIZE | DesktopInterop.SWP_NOACTIVATE | DesktopInterop.SWP_FRAMECHANGED);

        return true;
    }

    /// <summary>
    /// Pushes the window to the bottom of the Z-order behind normal application windows.
    /// </summary>
    public void SendToBottom()
    {
        if (_hwnd != IntPtr.Zero && !_isDisposed)
        {
            DesktopInterop.SetWindowPos(
                _hwnd,
                DesktopInterop.HWND_BOTTOM,
                0, 0, 0, 0,
                DesktopInterop.SWP_NOMOVE | DesktopInterop.SWP_NOSIZE | DesktopInterop.SWP_NOACTIVATE);
        }
    }

    /// <summary>
    /// Subclassed window procedure to intercept shell events and enforce bottom Z-order.
    /// </summary>
    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        uint uMsg = (uint)msg;

        if (uMsg == _taskbarCreatedMsg)
        {
            // Explorer restarted: re-discover host and re-attach
            AttachToDesktop();
            ExplorerRestarted?.Invoke();

            // Re-verify after shell finishes initialization (e.g. WorkerW spawn lag)
            _ = Task.Delay(500).ContinueWith(_ =>
            {
                if (!_isDisposed)
                {
                    _window.Dispatcher.Invoke(() =>
                    {
                        if (!_isDisposed)
                        {
                            AttachToDesktop();
                            ExplorerRestarted?.Invoke();
                        }
                    });
                }
            });

            handled = true;
            return IntPtr.Zero;
        }

        switch (uMsg)
        {
            case DesktopInterop.WM_WINDOWPOSCHANGING:
                if (_hostInfo.HostType == DesktopHostType.BottomPinnedFallback && lParam != IntPtr.Zero)
                {
                    // Enforce that window stays at bottom of Z-order
                    var winPos = Marshal.PtrToStructure<DesktopInterop.WINDOWPOS>(lParam);
                    winPos.hwndInsertAfter = DesktopInterop.HWND_BOTTOM;
                    Marshal.StructureToPtr(winPos, lParam, false);
                }
                break;

            case DesktopInterop.WM_DISPLAYCHANGE:
            case DesktopInterop.WM_SETTINGCHANGE:
                DisplayChanged?.Invoke();
                break;
        }

        return IntPtr.Zero;
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume && !_isDisposed)
        {
            _window.Dispatcher.Invoke(() =>
            {
                if (!_isDisposed)
                {
                    AttachToDesktop();
                    DisplayChanged?.Invoke();
                }
            });
        }
    }

    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        if (e.Reason == SessionSwitchReason.SessionUnlock && !_isDisposed)
        {
            _window.Dispatcher.Invoke(() =>
            {
                if (!_isDisposed)
                {
                    AttachToDesktop();
                    DisplayChanged?.Invoke();
                }
            });
        }
    }

    public void Dispose()
    {
        if (!_isDisposed)
        {
            _isDisposed = true;

            // Unsubscribe system events
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            SystemEvents.SessionSwitch -= OnSessionSwitch;

            if (_hwndSource != null && _hookAdded)
            {
                _hwndSource.RemoveHook(WndProc);
                _hookAdded = false;
                _hwndSource = null;
            }
        }
        GC.SuppressFinalize(this);
    }
}
