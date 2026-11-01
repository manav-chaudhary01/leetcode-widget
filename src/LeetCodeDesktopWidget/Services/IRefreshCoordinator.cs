using System;
using System.Threading;
using System.Threading.Tasks;

namespace LeetCodeDesktopWidget.Services;

public interface IRefreshCoordinator : IDisposable
{
    TimeSpan Interval { get; set; }
    bool IsRunning { get; }
    bool IsRefreshing { get; }

    void Start(Func<CancellationToken, Task> refreshCallback);
    void Stop();
    Task<bool> TriggerRefreshAsync(bool isManual = true);
}
