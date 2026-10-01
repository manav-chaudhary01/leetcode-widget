using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace LeetCodeDesktopWidget.Desktop;

/// <summary>
/// Manages window styles, Z-order positioning, Win32 message hooks, and lifecycle events.
/// </summary>
public class DesktopWindowManager : IDisposable
{
    private readonly Window _window;
    private IntPtr _hwnd = IntPtr.Zero;
    private HwndSource? _hwndSource;
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
    }

    /// <summary>
    /// Attaches the target WPF window to the Windows desktop wallpaper layer.
    /// </summary>
    public bool AttachToDesktop()
    {
        _hwnd = new WindowInteropHelper(_window).EnsureHandle();
        _hwndSource = HwndSource.FromHwnd(_hwnd);
        _hwndSource?.AddHook(WndProc);

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
            // Fallback: Pin to bottom of Z-order
            SendToBottom();
            _hostInfo.IsAttached = false;
        }

        // Apply updated frame and refresh
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
        if (_hwnd != IntPtr.Zero)
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
            ExplorerRestarted?.Invoke();
            AttachToDesktop();
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

    public void Dispose()
    {
        if (!_isDisposed)
        {
            if (_hwndSource != null)
            {
                _hwndSource.RemoveHook(WndProc);
                _hwndSource = null;
            }
            _isDisposed = true;
        }
        GC.SuppressFinalize(this);
    }
}
