using System;
using System.Text;

namespace LeetCodeDesktopWidget.Desktop;

/// <summary>
/// Result of the desktop window discovery.
/// </summary>
public enum DesktopHostType
{
    WorkerW,
    Progman,
    BottomPinnedFallback
}

/// <summary>
/// Encapsulates the discovered desktop handles and hosting mode.
/// </summary>
public class DesktopHostInfo
{
    public IntPtr TargetParentHandle { get; init; } = IntPtr.Zero;
    public IntPtr ProgmanHandle { get; init; } = IntPtr.Zero;
    public IntPtr ShellDefViewHandle { get; init; } = IntPtr.Zero;
    public DesktopHostType HostType { get; init; } = DesktopHostType.BottomPinnedFallback;
    public bool IsAttached { get; set; }
}

/// <summary>
/// Responsible for finding the Windows Shell wallpaper/desktop window hierarchy.
/// </summary>
public static class DesktopHost
{
    /// <summary>
    /// Discovers the appropriate parent window for hosting a desktop-layer widget.
    /// Sends the 0x052C message to Progman to split wallpaper and icons if needed.
    /// </summary>
    public static DesktopHostInfo DiscoverDesktopHost()
    {
        IntPtr progman = DesktopInterop.FindWindow("Progman", null);
        if (progman == IntPtr.Zero)
        {
            progman = DesktopInterop.GetShellWindow();
        }

        IntPtr shellDefView = IntPtr.Zero;
        IntPtr targetWorkerW = IntPtr.Zero;

        if (progman != IntPtr.Zero)
        {
            // Direct Progman to spawn WorkerW behind desktop icons
            DesktopInterop.SendMessageTimeout(
                progman,
                DesktopInterop.WM_SPAWN_WORKER,
                new IntPtr(0xD),
                IntPtr.Zero,
                DesktopInterop.SMTO_NORMAL,
                1000,
                out _);

            // Check if SHELLDLL_DefView is directly inside Progman
            shellDefView = DesktopInterop.FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);

            // Enumerate top-level windows to locate WorkerW
            DesktopInterop.EnumWindows((hwnd, _) =>
            {
                StringBuilder sb = new(256);
                DesktopInterop.GetClassName(hwnd, sb, 256);
                if (sb.ToString() == "WorkerW")
                {
                    IntPtr childDefView = DesktopInterop.FindWindowEx(hwnd, IntPtr.Zero, "SHELLDLL_DefView", null);
                    if (childDefView != IntPtr.Zero)
                    {
                        shellDefView = childDefView;
                        // On some Windows 10/11 versions, the wallpaper WorkerW is the sibling immediately behind
                        targetWorkerW = DesktopInterop.FindWindowEx(IntPtr.Zero, hwnd, "WorkerW", null);
                    }
                }
                return true;
            }, IntPtr.Zero);
        }

        // Determine best target handle
        if (targetWorkerW != IntPtr.Zero)
        {
            return new DesktopHostInfo
            {
                TargetParentHandle = targetWorkerW,
                ProgmanHandle = progman,
                ShellDefViewHandle = shellDefView,
                HostType = DesktopHostType.WorkerW
            };
        }

        if (progman != IntPtr.Zero)
        {
            return new DesktopHostInfo
            {
                TargetParentHandle = progman,
                ProgmanHandle = progman,
                ShellDefViewHandle = shellDefView,
                HostType = DesktopHostType.Progman
            };
        }

        return new DesktopHostInfo
        {
            TargetParentHandle = IntPtr.Zero,
            ProgmanHandle = IntPtr.Zero,
            ShellDefViewHandle = IntPtr.Zero,
            HostType = DesktopHostType.BottomPinnedFallback
        };
    }
}
