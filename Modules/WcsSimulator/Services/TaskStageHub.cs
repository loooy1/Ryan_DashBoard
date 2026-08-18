using GRCS.Dashboard.Modules.WcsSimulator.Models;
using Microsoft.JSInterop;

namespace GRCS.Dashboard.Modules.WcsSimulator.Services;

/// <summary>
/// 任务阶段事件共享服务（scoped = 每个浏览器标签页一个实例）。
/// 通过 SignalR 长连接（WCS 后端 /hubs/task-stages）实时接收任务阶段事件，
/// 取代旧版的 HTTP 轮询（/api/wcs/task-stages?sinceId=N）。
///
/// ── 数据流 ──
/// 1. 连接建立（EnsureStartedAsync，MainLayout 注入调用）→ 后端回放当前事件快照（EventsReset）；
/// 2. 后端每收到一条 GRCS task_stage_change 即广播 EventAdded → 本服务合并进缓存并触发 Changed；
/// 3. 其它标签页删除任务 → 广播 TaskRemoved → 同步清本地缓存（防等待者基于陈旧 FINISHED 误判）。
/// 4. 断线自动重连（JS 端 withAutomaticReconnect），重连成功后再收 EventsReset 对账。
///
/// 消费方全部零 HTTP：事件看板、信号交互页（到达/移除/分拣卡片）、两段式任务等待（WaitFinishedAsync）。
/// 连接状态用 IsOnline 暴露（true = 实时推送可用，与后端存活判定无关，后端存活判定走 BackendHealthService）。
/// </summary>
public class TaskStageHub : IDisposable
{
    private const string WcsUrlKey = "grcs_wcs_url";
    private const string DefaultWcsUrl = "http://localhost:8230";
    private const int MaxCache = 1000;          // 前端缓存上限（足够覆盖活跃任务）

    private readonly IJSRuntime _js;
    private readonly LocalStoreService _store;
    private readonly object _lock = new();
    private DotNetObjectReference<TaskStageHub>? _ref;
    private bool _started;
    private List<StageChangeEvent> _events = [];
    private readonly HashSet<string> _finished = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>事件缓存（按到达顺序，最多 MaxCache 条）。</summary>
    public IReadOnlyList<StageChangeEvent> Events => _events;

    /// <summary>已 FINISHED 的任务号集合（大小写不敏感）。</summary>
    public HashSet<string> FinishedTaskIds => _finished;

    /// <summary>SignalR 连接是否可用（true = 实时推送中）。</summary>
    public bool IsOnline { get; private set; }

    /// <summary>新事件合并/状态变化时触发（订阅者据此刷新 UI / 唤醒等待者）。</summary>
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

    /// <summary>后端广播：单条新事件。</summary>
    [JSInvokable]
    public void OnEventAdded(StageChangeEvent evt)
    {
        if (evt == null || string.IsNullOrEmpty(evt.TaskId)) return;
        lock (_lock)
        {
            _events.Add(evt);
            if (_events.Count > MaxCache) _events.RemoveRange(0, _events.Count - MaxCache);
            if (string.Equals(evt.Stage, "FINISHED", StringComparison.OrdinalIgnoreCase))
                _finished.Add(evt.TaskId);
        }
        Changed?.Invoke();
    }

    /// <summary>后端广播：全量快照（连接建立/清空后对账）→ 整表替换。</summary>
    [JSInvokable]
    public void OnEventsReset(List<StageChangeEvent>? evts)
    {
        lock (_lock)
        {
            _events = evts ?? [];
            if (_events.Count > MaxCache) _events.RemoveRange(0, _events.Count - MaxCache);
            _finished.Clear();
            foreach (var e in _events)
                if (string.Equals(e.Stage, "FINISHED", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(e.TaskId))
                    _finished.Add(e.TaskId);
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
        IsOnline = state == "connected" || state == "reconnecting";
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

    /// <summary>删除某任务的事件后调用：同步清掉本地缓存，防止等待者基于陈旧数据误判。</summary>
    public void RemoveTask(string taskId)
    {
        lock (_lock)
        {
            _events.RemoveAll(e => string.Equals(e.TaskId, taskId, StringComparison.OrdinalIgnoreCase));
            _finished.Remove(taskId);
        }
        Changed?.Invoke();
    }

    /// <summary>清空全部事件后调用：本地缓存同步清空。</summary>
    public void ClearAll()
    {
        lock (_lock) { _events.Clear(); _finished.Clear(); }
        Changed?.Invoke();
    }

    public void Dispose()
    {
        try { _js.InvokeVoidAsync("grcsTaskStage.disconnect"); } catch { }
        _ref?.Dispose();
        _ref = null;
    }
}