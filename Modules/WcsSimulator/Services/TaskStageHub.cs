using GRCS.Dashboard.Modules.WcsSimulator.Models;
using GRCS.Dashboard.Modules.WcsSimulator.Models.TWD;
using Microsoft.JSInterop;

namespace GRCS.Dashboard.Modules.WcsSimulator.Services;

/// <summary>
/// 任务记录共享服务（scoped = 每个浏览器标签页一个实例）。
/// 通过 SignalR 长连接（WCS 后端 /hubs/task-stages）实时接收合并表（task_records）全量记录，
/// 取代旧版的 HTTP 轮询（/api/wcs/task-stages?sinceId=N）与台账 HTTP 拉取。
///
/// ── 数据流 ──
/// 1. 连接建立（EnsureStartedAsync，MainLayout 注入调用）→ 后端回放全表快照（EventsReset）；
/// 2. 后端每写入一条记录（创建行 CREATED 或阶段行 START/LOAD_FINISH/FINISHED）即广播 EventAdded
///    → 本服务合并进缓存并触发 Changed；
/// 3. 其它标签页删除任务/清空 → 广播 TaskRemoved / EventsReset(空) → 同步本地缓存。
/// 4. 断线自动重连（JS 端 withAutomaticReconnect），重连成功后再收 EventsReset 对账。
///
/// 消费方全部零 HTTP：任务看板（创建行骨架 + 阶段时间线）、信号交互页（到达/移除/分拣卡片）、
/// 两段式任务等待（WaitFinishedAsync）。创建行也走本缓存，前端不再单独 HTTP 拉台账。
/// 连接状态用 IsOnline 暴露（true = 实时推送可用，与后端存活判定无关，后端存活判定走 BackendHealthService）。
/// </summary>
public class TaskStageHub : IDisposable
{
    private const string WcsUrlKey = "grcs_wcs_url";
    private const string DefaultWcsUrl = "http://localhost:8230";
    private const int MaxCache = 10000;         // 前端缓存上限（与后端 task_records 表上限一致）

    private readonly IJSRuntime _js;
    private readonly LocalStoreService _store;
    private readonly object _lock = new();
    private DotNetObjectReference<TaskStageHub>? _ref;
    private bool _started;
    private List<TaskRecord> _records = [];
    private readonly HashSet<string> _finished = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>阶段视图（过滤 CREATED 创建行，按到达顺序）。</summary>
    public IReadOnlyList<StageChangeEvent> Events
    {
        get { lock (_lock) { return _records.Where(r => !r.IsCreated).Select(r => r.ToStageEvent()).ToList(); } }
    }

    /// <summary>创建视图（stage=CREATED，替代原 TaskLedgerService.GetAsync 读台账；最新在前）。</summary>
    public IReadOnlyList<TaskLedgerEntry> CreatedTasks
    {
        get
        {
            lock (_lock)
            {
                return _records.Where(r => r.IsCreated).Reverse().Select(r => r.ToLedgerEntry()).ToList();
            }
        }
    }

    /// <summary>某任务的全部记录（创建行 + 阶段行，按到达顺序，用于看板时间线）。</summary>
    public IReadOnlyList<TaskRecord> StagesOf(string taskId)
    {
        lock (_lock)
        {
            return _records
                .Where(r => string.Equals(r.TaskId, taskId, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
    }

    /// <summary>已 FINISHED 的任务号集合（大小写不敏感）。</summary>
    public HashSet<string> FinishedTaskIds => _finished;

    /// <summary>新记录合并/状态变化时触发（订阅者据此刷新 UI / 唤醒等待者）。</summary>
    public event Action? Changed;

    public TaskStageHub(IJSRuntime js, LocalStoreService store)
    {
        _js = js;
        _store = store;
    }

    /// <summary>建立 SignalR 连接（幂等；MainLayout 注入时调用，保证每个标签页常驻）。</summary>
    public async Task EnsureStartedAsync()
    {
        if (_started) return;
        _started = true;
        try
        {
            _ref = DotNetObjectReference.Create(this);
            await _js.InvokeVoidAsync("grcsTaskStage.setRef", _ref);
            await _js.InvokeVoidAsync("grcsTaskStage.connect", ResolveHubUrl());
        }
        catch (Exception ex)
        {
            try { await _js.InvokeVoidAsync("console.error", $"[TaskStageHub] 启动失败: {ex.Message}"); } catch { }
        }
    }

    private string ResolveHubUrl()
    {
        var url = _store[WcsUrlKey];
        var baseUrl = string.IsNullOrEmpty(url) || url == "null" ? DefaultWcsUrl : url;
        return baseUrl.TrimEnd('/') + "/hubs/task-stages";
    }

    /// <summary>后端广播：单条新记录（创建行或阶段行）。</summary>
    [JSInvokable]
    public void OnEventAdded(TaskRecord evt)
    {
        if (evt == null || string.IsNullOrEmpty(evt.TaskId)) return;
        lock (_lock)
        {
            _records.Add(evt);
            if (_records.Count > MaxCache) _records.RemoveRange(0, _records.Count - MaxCache);
            if (string.Equals(evt.Stage, "FINISHED", StringComparison.OrdinalIgnoreCase))
                _finished.Add(evt.TaskId);
        }
        Changed?.Invoke();
    }

    /// <summary>后端广播：全表快照（连接建立/清空后对账）→ 整表替换。</summary>
    [JSInvokable]
    public void OnEventsReset(List<TaskRecord>? records)
    {
        lock (_lock)
        {
            _records = records ?? [];
            if (_records.Count > MaxCache) _records.RemoveRange(0, _records.Count - MaxCache);
            _finished.Clear();
            foreach (var r in _records)
                if (string.Equals(r.Stage, "FINISHED", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(r.TaskId))
                    _finished.Add(r.TaskId);
        }
        Changed?.Invoke();
    }

    /// <summary>后端广播：某任务被删除（其它标签页操作）→ 同步本地缓存。</summary>
    [JSInvokable]
    public void OnTaskRemoved(string taskId)
    {
        if (string.IsNullOrEmpty(taskId)) return;
        RemoveTask(taskId);
    }

    /// <summary>JS 回报连接状态：connected / reconnecting / disconnected。</summary>
    [JSInvokable]
    public void OnStateChanged(string state)
    {
        Changed?.Invoke();
    }

    /// <summary>
    /// 等待任务到达 FINISHED 阶段（与后端进程内 WaitFinishedAsync 语义对齐，供前端两段式流程用）。
    /// 订阅 Changed 事件，FINISHED 到达即唤醒；订阅前/后各检查一次缓存，防竞态丢事件。
    /// </summary>
    public async Task WaitFinishedAsync(string taskId)
    {
        await EnsureStartedAsync();
        if (_finished.Contains(taskId)) return;

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Handler()
        {
            if (_finished.Contains(taskId)) tcs.TrySetResult();
        }

        Changed += Handler;
        try
        {
            if (_finished.Contains(taskId)) return;
            await tcs.Task;
        }
        finally
        {
            Changed -= Handler;
        }
    }

    /// <summary>删除某任务后调用：同步清掉本地缓存（全行：创建 + 阶段），防止等待者基于陈旧数据误判。</summary>
    public void RemoveTask(string taskId)
    {
        lock (_lock)
        {
            _records.RemoveAll(r => string.Equals(r.TaskId, taskId, StringComparison.OrdinalIgnoreCase));
            _finished.Remove(taskId);
        }
        Changed?.Invoke();
    }

    /// <summary>清空全部记录后调用：本地缓存同步清空。</summary>
    public void ClearAll()
    {
        lock (_lock) { _records.Clear(); _finished.Clear(); }
        Changed?.Invoke();
    }

    public void Dispose()
    {
        try { _js.InvokeVoidAsync("grcsTaskStage.disconnect"); } catch { }
        _ref?.Dispose();
        _ref = null;
    }
}