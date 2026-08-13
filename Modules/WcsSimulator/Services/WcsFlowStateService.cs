using System.Collections.Concurrent;

namespace GRCS.Dashboard.Modules.WcsSimulator.Services;

/// <summary>
/// 自动容器任务与信号交互页之间的信号状态桥：
/// 自动化任务下发段1 后等待"货物到达/移除信号已发送"，确认后才下发段2，
/// 保证时序是"货先走（信号确认），托后回（段2）"。
/// </summary>
public class WcsFlowStateService
{
    private readonly ConcurrentDictionary<string, TaskCompletionSource> _waiters = new();

    /// <summary>等待段1 任务（TaskId）的货物信号确认；已确认立即返回 true，超时返回 false。</summary>
    public async Task<bool> WaitSignalAsync(string seg1TaskId, TimeSpan timeout)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _waiters[seg1TaskId] = tcs;
        using var cts = new CancellationTokenSource(timeout);
        var done = await Task.WhenAny(tcs.Task, Task.Delay(Timeout.InfiniteTimeSpan, cts.Token));
        if (done != tcs.Task) _waiters.TryRemove(seg1TaskId, out _); // 超时清理，避免残留
        return done == tcs.Task;
    }

    /// <summary>货物信号发送成功（信号交互页确认后调用），放行对应段2。</summary>
    public void NotifySignalSent(string seg1TaskId)
    {
        if (_waiters.TryRemove(seg1TaskId, out var tcs)) tcs.TrySetResult();
    }
}
