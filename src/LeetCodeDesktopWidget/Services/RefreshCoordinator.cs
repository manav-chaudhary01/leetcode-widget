using System;
using System.Threading;
using System.Threading.Tasks;

namespace LeetCodeDesktopWidget.Services;

/// <summary>
/// Orchestrates periodic and manual synchronization with strict concurrency protection.
/// Ensures at most one refresh operation executes at any time.
/// </summary>
public class RefreshCoordinator : IRefreshCoordinator
{
    private readonly object _syncRoot = new();
    private Task<bool>? _activeRefreshTask;
    private CancellationTokenSource? _cts;
    private Timer? _timer;
    private Func<CancellationToken, Task>? _callback;
    private bool _isDisposed;

    public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(60);
    public bool IsRunning { get; private set; }
    public bool IsRefreshing
    {
        get
        {
            lock (_syncRoot)
            {
                return _activeRefreshTask != null && !_activeRefreshTask.IsCompleted;
            }
        }
    }

    public void Start(Func<CancellationToken, Task> refreshCallback)
    {
        lock (_syncRoot)
        {
            if (IsRunning) return;

            _callback = refreshCallback ?? throw new ArgumentNullException(nameof(refreshCallback));
            _cts = new CancellationTokenSource();
            IsRunning = true;

            // Schedule periodic timer
            _timer = new Timer(
                OnTimerCallback,
                null,
                Interval,
                Interval);
        }
    }

    private void OnTimerCallback(object? state)
    {
        if (!IsRunning || _isDisposed) return;

        // Fire-and-forget safe wrapper with concurrency check
        _ = TriggerRefreshAsync(isManual: false);
    }

    public Task<bool> TriggerRefreshAsync(bool isManual = true)
    {
        lock (_syncRoot)
        {
            if (_callback == null || _isDisposed)
            {
                return Task.FromResult(false);
            }

            if (_activeRefreshTask != null && !_activeRefreshTask.IsCompleted)
            {
                // Refresh already in progress:
                // For automatic/periodic triggers, ignore to prevent duplicate load.
                // For manual triggers, coalesce with in-flight task without starting a second HTTP request.
                return isManual ? _activeRefreshTask : Task.FromResult(false);
            }

            _activeRefreshTask = ExecuteRefreshCoreAsync();
            return _activeRefreshTask;
        }
    }

    private async Task<bool> ExecuteRefreshCoreAsync()
    {
        try
        {
            CancellationToken token;
            lock (_syncRoot)
            {
                token = _cts?.Token ?? CancellationToken.None;
            }

            if (token.IsCancellationRequested) return false;

            if (_callback != null)
            {
                await _callback(token).ConfigureAwait(false);
                return true;
            }
            return false;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch
        {
            // Unhandled exceptions inside refresh callback must not escape coordinator
            return false;
        }
    }

    public void Stop()
    {
        lock (_syncRoot)
        {
            if (!IsRunning) return;

            IsRunning = false;
            _timer?.Change(Timeout.Infinite, Timeout.Infinite);
            _timer?.Dispose();
            _timer = null;

            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
        }
    }

    public void Dispose()
    {
        if (!_isDisposed)
        {
            Stop();
            _isDisposed = true;
        }
        GC.SuppressFinalize(this);
    }
}
