using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using LeetCodeDesktopWidget.Desktop;
using Xunit;
using Xunit.Abstractions;

namespace LeetCodeDesktopWidget.Tests;

public class DesktopIntegrationTests
{
    private readonly ITestOutputHelper _output;

    public DesktopIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void Win32Constants_HaveCorrectValues()
    {
        Assert.Equal(0x00000080L, DesktopInterop.WS_EX_TOOLWINDOW);
        Assert.Equal(0x08000000L, DesktopInterop.WS_EX_NOACTIVATE);
        Assert.Equal(0x40000000L, DesktopInterop.WS_CHILD);
        Assert.Equal(0x0046u, DesktopInterop.WM_WINDOWPOSCHANGING);
        Assert.Equal(new IntPtr(1), DesktopInterop.HWND_BOTTOM);
    }

    [Fact]
    public void DiscoverDesktopHost_ReturnsValidHostInfo()
    {
        var hostInfo = DesktopHost.DiscoverDesktopHost();
        Assert.NotNull(hostInfo);
        _output.WriteLine($"Discovered HostType: {hostInfo.HostType}");
        _output.WriteLine($"TargetParentHandle: 0x{hostInfo.TargetParentHandle.ToInt64():X}");
        _output.WriteLine($"ProgmanHandle: 0x{hostInfo.ProgmanHandle.ToInt64():X}");
        _output.WriteLine($"ShellDefViewHandle: 0x{hostInfo.ShellDefViewHandle.ToInt64():X}");
        Assert.True(Enum.IsDefined(typeof(DesktopHostType), hostInfo.HostType));
    }

    [Fact]
    public void DesktopWindowManager_AppliesToolWindowAndNoActivateStyles()
    {
        Exception? threadException = null;
        var thread = new Thread(() =>
        {
            try
            {
                var window = new Window
                {
                    Width = 200,
                    Height = 100,
                    WindowStyle = WindowStyle.None,
                    ShowInTaskbar = false
                };

                using var manager = new DesktopWindowManager(window);
                var attached = manager.AttachToDesktop();
                Assert.True(attached);

                var hwnd = new WindowInteropHelper(window).Handle;
                Assert.NotEqual(IntPtr.Zero, hwnd);

                var exStyle = DesktopInterop.GetWindowLong(hwnd, DesktopInterop.GWL_EXSTYLE);
                Assert.True((exStyle & DesktopInterop.WS_EX_TOOLWINDOW) != 0, "Window must have WS_EX_TOOLWINDOW style");
                Assert.True((exStyle & DesktopInterop.WS_EX_NOACTIVATE) != 0, "Window must have WS_EX_NOACTIVATE style");

                _output.WriteLine($"HostType in WindowManager: {manager.CurrentHostInfo.HostType}");
                _output.WriteLine($"IsAttached: {manager.CurrentHostInfo.IsAttached}");
                _output.WriteLine($"Final ExStyle: 0x{exStyle:X8}");

                window.Close();
            }
            catch (Exception ex)
            {
                threadException = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(5000);

        if (threadException != null)
        {
            throw threadException;
        }
    }

    [Fact]
    public void DesktopWindowManager_HandlesDesktopAttachmentGracefully()
    {
        Exception? threadException = null;
        var thread = new Thread(() =>
        {
            try
            {
                IntPtr hDesk = DesktopInterop.OpenDesktop("Default", 0, false, 0x1FF);
                int openErr = Marshal.GetLastWin32Error();
                bool setOk = false;
                int setErr = 0;
                if (hDesk != IntPtr.Zero)
                {
                    setOk = DesktopInterop.SetThreadDesktop(hDesk);
                    setErr = Marshal.GetLastWin32Error();
                }

                _output.WriteLine($"OpenDesktop handle: {hDesk}, Err: {openErr}; SetThreadDesktop: {setOk}, Err: {setErr}");

                var window = new Window
                {
                    Width = 200,
                    Height = 100,
                    WindowStyle = WindowStyle.None,
                    ShowInTaskbar = false
                };

                using var manager = new DesktopWindowManager(window);
                bool attached = manager.AttachToDesktop();
                Assert.True(attached);

                _output.WriteLine($"HostType: {manager.CurrentHostInfo.HostType}");
                _output.WriteLine($"TargetHandle: 0x{manager.CurrentHostInfo.TargetParentHandle.ToInt64():X}");
                _output.WriteLine($"IsAttached: {manager.CurrentHostInfo.IsAttached}");

                // Ensure robust fallback even in sandboxed runner environments:
                // Either successfully parented to Progman/WorkerW OR safely bottom-pinned
                if (manager.CurrentHostInfo.TargetParentHandle != IntPtr.Zero)
                {
                    Assert.True(manager.CurrentHostInfo.IsAttached);
                    Assert.NotEqual(DesktopHostType.BottomPinnedFallback, manager.CurrentHostInfo.HostType);
                }
                else
                {
                    Assert.False(manager.CurrentHostInfo.IsAttached);
                    Assert.Equal(DesktopHostType.BottomPinnedFallback, manager.CurrentHostInfo.HostType);
                }

                window.Close();
                if (hDesk != IntPtr.Zero) DesktopInterop.CloseDesktop(hDesk);
            }
            catch (Exception ex)
            {
                threadException = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(5000);

        if (threadException != null)
        {
            throw threadException;
        }
    }
}
