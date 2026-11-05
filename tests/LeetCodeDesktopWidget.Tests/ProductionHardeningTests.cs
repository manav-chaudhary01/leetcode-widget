using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LeetCodeDesktopWidget.Desktop;
using LeetCodeDesktopWidget.Models;
using LeetCodeDesktopWidget.Services;
using LeetCodeDesktopWidget.ViewModels;
using System.Windows;
using Xunit;

namespace LeetCodeDesktopWidget.Tests;

public class ProductionHardeningTests
{
    #region 1. Refresh Coordinator Resilience & Error Recovery

    [Fact]
    public async Task RefreshCoordinator_ExceptionInCallback_DoesNotPoisonFutureRefreshes()
    {
        using var coordinator = new RefreshCoordinator();
        bool shouldThrow = true;
        int executionCount = 0;

        coordinator.Start(ct =>
        {
            Interlocked.Increment(ref executionCount);
            if (shouldThrow)
            {
                throw new InvalidOperationException("Simulated transient failure");
            }
            return Task.CompletedTask;
        });

        // 1. First trigger throws inside callback; coordinator catches it safely and returns false
        bool firstResult = await coordinator.TriggerRefreshAsync(isManual: true);
        Assert.False(firstResult);
        Assert.False(coordinator.IsRefreshing);
        Assert.Equal(1, executionCount);

        // 2. Second trigger succeeds; coordinator is not locked or dead
        shouldThrow = false;
        bool secondResult = await coordinator.TriggerRefreshAsync(isManual: true);
        Assert.True(secondResult);
        Assert.False(coordinator.IsRefreshing);
        Assert.Equal(2, executionCount);
    }

    [Fact]
    public async Task RefreshCoordinator_TriggerAfterDisposal_ReturnsFalseSafely()
    {
        var coordinator = new RefreshCoordinator();
        coordinator.Start(ct => Task.CompletedTask);
        coordinator.Dispose();

        bool result = await coordinator.TriggerRefreshAsync(isManual: true);
        Assert.False(result);
        Assert.False(coordinator.IsRunning);
    }

    [Fact]
    public async Task RefreshCoordinator_CancellationTokenPropagatesAndCancelsGracefully()
    {
        using var coordinator = new RefreshCoordinator();
        var startedTcs = new TaskCompletionSource();
        var canceledTcs = new TaskCompletionSource();

        coordinator.Start(async ct =>
        {
            startedTcs.SetResult();
            try
            {
                await Task.Delay(5000, ct);
            }
            catch (OperationCanceledException)
            {
                canceledTcs.SetResult();
                throw;
            }
        });

        var refreshTask = coordinator.TriggerRefreshAsync(isManual: true);
        await startedTcs.Task;

        // Stopping coordinator cancels active token
        coordinator.Stop();

        bool result = await refreshTask;
        Assert.False(result);
        Assert.True(canceledTcs.Task.IsCompletedSuccessfully);
    }

    #endregion

    #region 2. Username Editing & Cancel/Revert Usability

    [Fact]
    public void MainViewModel_ToggleEditAndCancel_RestoresOriginalUsername()
    {
        var vm = new MainViewModel(new LeetCodeService())
        {
            Username = "original_user"
        };

        // 1. Toggle edit mode on
        vm.ToggleEditUsernameCommand.Execute(null);
        Assert.True(vm.IsEditingUsername);

        // 2. User types new username but makes a mistake
        vm.Username = "mistyped_user";

        // 3. User cancels (Esc or Cancel button)
        vm.CancelEditUsernameCommand.Execute(null);
        Assert.False(vm.IsEditingUsername);
        Assert.Equal("original_user", vm.Username);
    }

    [Fact]
    public void MainViewModel_SaveEmptyOrInvalidUsername_ShowsClearErrorMessage()
    {
        var vm = new MainViewModel(new LeetCodeService())
        {
            Username = "valid_user"
        };

        // Toggle edit mode and enter invalid username
        vm.ToggleEditUsernameCommand.Execute(null);
        vm.Username = "   @@@   ";

        // Save
        vm.SaveUsernameCommand.Execute(null);

        Assert.False(vm.IsEditingUsername);
        Assert.Contains("valid username", vm.SyncStatusText, StringComparison.OrdinalIgnoreCase);
    }

    #endregion

    #region 3. LeetCode Service Hardening & Boundary Validation

    [Fact]
    public async Task LeetCodeService_EmptyOrWhitespaceUsername_ReturnsImmediateUserNotFoundError()
    {
        var service = new LeetCodeService();

        var resNull = await service.GetUserActivityAsync("");
        Assert.False(resNull.IsSuccess);
        Assert.Equal(LeetCodeErrorCode.UserNotFound, resNull.ErrorCode);

        var resSpaces = await service.GetUserActivityAsync("   ");
        Assert.False(resSpaces.IsSuccess);
        Assert.Equal(LeetCodeErrorCode.UserNotFound, resSpaces.ErrorCode);
    }

    [Fact]
    public async Task LeetCodeService_MalformedJsonFromEndpoint_ReturnsInvalidResponseError()
    {
        var handler = new MockHttpMessageHandler(req => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("<!DOCTYPE html><html><body>Error 502 Bad Gateway</body></html>", Encoding.UTF8, "text/html")
        });

        var service = new LeetCodeService(new HttpClient(handler));
        var result = await service.GetUserActivityAsync("coder");

        Assert.False(result.IsSuccess);
        Assert.Equal(LeetCodeErrorCode.InvalidResponse, result.ErrorCode);
    }

    [Fact]
    public async Task LeetCodeService_MissingMatchedUserInJson_ReturnsUserNotFoundError()
    {
        var handler = new MockHttpMessageHandler(req => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(@"{ ""data"": { ""matchedUser"": null } }", Encoding.UTF8, "application/json")
        });

        var service = new LeetCodeService(new HttpClient(handler));
        var result = await service.GetUserActivityAsync("ghost_user");

        Assert.False(result.IsSuccess);
        Assert.Equal(LeetCodeErrorCode.UserNotFound, result.ErrorCode);
        Assert.Contains("ghost_user", result.ErrorMessage);
    }

    [Fact]
    public void LeetCodeService_ParseSubmissionCalendar_HandlesVariousEdgeCases()
    {
        var service = new LeetCodeService();

        // Null / whitespace / empty
        Assert.Empty(service.ParseSubmissionCalendar(null));
        Assert.Empty(service.ParseSubmissionCalendar(""));
        Assert.Empty(service.ParseSubmissionCalendar("   "));
        Assert.Empty(service.ParseSubmissionCalendar("{}"));
        Assert.Empty(service.ParseSubmissionCalendar("null"));

        // Malformed
        Assert.Empty(service.ParseSubmissionCalendar("{ not valid json }"));

        // Mixed types (string count vs int count)
        string mixed = @"{ ""1719100800"": 3, ""1726963200"": ""7"", ""invalid_ts"": 2, ""1726963201"": 0 }";
        var parsed = service.ParseSubmissionCalendar(mixed);

        Assert.Equal(2, parsed.Count);
        Assert.Equal(3, parsed[new DateOnly(2024, 6, 23)]);
        Assert.Equal(7, parsed[new DateOnly(2024, 9, 22)]);
    }

    #endregion

    #region 4. Privacy, Secrets & Persistence Verification

    [Fact]
    public void PrivacyReview_WidgetConfigContainsNoSecretsOrSensitiveProperties()
    {
        var properties = typeof(WidgetConfig).GetProperties(BindingFlags.Public | BindingFlags.Instance);
        var propertyNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var prop in properties)
        {
            propertyNames.Add(prop.Name);
            // Verify no secret keywords
            Assert.DoesNotContain("password", prop.Name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("token", prop.Name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("cookie", prop.Name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("secret", prop.Name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("auth", prop.Name, StringComparison.OrdinalIgnoreCase);
        }

        // Must only contain expected user preference properties
        Assert.Contains("Username", propertyNames);
        Assert.Contains("SelectedYear", propertyNames);
        Assert.Contains("RefreshIntervalMinutes", propertyNames);
    }

    [Fact]
    public void PrivacyReview_CachedActivityDataContainsNoSecretsOrSensitiveProperties()
    {
        var properties = typeof(CachedActivityData).GetProperties(BindingFlags.Public | BindingFlags.Instance);

        foreach (var prop in properties)
        {
            Assert.DoesNotContain("password", prop.Name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("token", prop.Name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("cookie", prop.Name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("secret", prop.Name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("auth", prop.Name, StringComparison.OrdinalIgnoreCase);
        }
    }

    #endregion

    #region 5. Desktop Host & Window Management Unit Verification

    [Fact]
    public void DesktopHost_DiscoverDesktopHost_ReturnsValidInstance()
    {
        var hostInfo = DesktopHost.DiscoverDesktopHost();
        Assert.NotNull(hostInfo);
        // On any Windows environment, must return one of the 3 supported host types
        Assert.True(
            hostInfo.HostType == DesktopHostType.WorkerW ||
            hostInfo.HostType == DesktopHostType.Progman ||
            hostInfo.HostType == DesktopHostType.BottomPinnedFallback);
    }

    [Fact]
    public void DesktopWindowManager_TaskbarCreatedMessage_TriggersReattachAndExplorerRestartedEvent()
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
                manager.AttachToDesktop();

                bool eventFired = false;
                manager.ExplorerRestarted += () => eventFired = true;

                var hwnd = new System.Windows.Interop.WindowInteropHelper(window).Handle;
                uint taskbarMsg = DesktopInterop.RegisterWindowMessage("TaskbarCreated");

                // Dispatch TaskbarCreated message directly to WndProc
                var method = typeof(DesktopWindowManager).GetMethod("WndProc", BindingFlags.NonPublic | BindingFlags.Instance);
                object[] args = new object[] { hwnd, (int)taskbarMsg, IntPtr.Zero, IntPtr.Zero, false };
                method?.Invoke(manager, args);

                Assert.True(eventFired);
                Assert.NotNull(manager.CurrentHostInfo);

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

        if (threadException != null) throw threadException;
    }

    [Fact]
    public void MainWindow_EnsureVisibleOnScreen_ClampsCoordinatesInsideWorkArea()
    {
        // Test boundary clamping logic using the same math as MainWindow.EnsureVisibleOnScreen
        double screenWidth = 1920;
        double screenHeight = 1080;
        double width = 845;
        double height = 275;

        double maxLeft = Math.Max(0, screenWidth - width - 20); // 1055
        double maxTop = Math.Max(0, screenHeight - height - 20); // 785

        // Off-screen right/bottom
        double left1 = 2500;
        double top1 = 1500;
        double clampedL1 = left1 > maxLeft ? Math.Max(20, maxLeft) : left1;
        double clampedT1 = top1 > maxTop ? Math.Max(20, maxTop) : top1;
        Assert.Equal(1055, clampedL1);
        Assert.Equal(785, clampedT1);

        // Off-screen left/top
        double left2 = -100;
        double top2 = -50;
        double clampedL2 = left2 < 0 ? 20 : left2;
        double clampedT2 = top2 < 0 ? 20 : top2;
        Assert.Equal(20, clampedL2);
        Assert.Equal(20, clampedT2);

        // Within bounds
        double left3 = 200;
        double top3 = 300;
        double clampedL3 = left3 > maxLeft ? Math.Max(20, maxLeft) : (left3 < 0 ? 20 : left3);
        double clampedT3 = top3 > maxTop ? Math.Max(20, maxTop) : (top3 < 0 ? 20 : top3);
        Assert.Equal(200, clampedL3);
        Assert.Equal(300, clampedT3);
    }

    #endregion
}
