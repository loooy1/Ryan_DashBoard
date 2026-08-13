using System.Text.Json;
using GRCS.Dashboard.Modules.WcsSimulator.Models;
using Microsoft.JSInterop;

namespace GRCS.Dashboard.Modules.WcsSimulator.Services;

/// <summary>
/// 任务阶段事件共享轮询器（scoped = 每个浏览器标签页一个实例）。
///
/// ── 为什么存在 ──
/// 优化前有 6 处各自轮询 /api/wcs/task-stages：AutoRunService（5s 一轮）、SignalAutoService（3s 一轮）、
/// ContainerTaskService（每个在途任务 1s×N 个并发）、StationLockService（每次分配站点前）、
/// 任务看板页（3s 定时器）、信号交互页（每次刷新一次全量）。自动化跑起来时每秒 N+ 个重复 HTTP，
/// 自己跟自己抢带宽，页面响应变慢。本服务把这 N 个轮询收敛成全应用唯一的一个。
///
/// ── 数据流 ──
/// 1. 首次启动拉全量（WCS 后端默认返回最近 200 条），此后每轮带 sinceId 增量拉取（配合
///    /api/wcs/task-stages?sinceId=N，只传增量几条，见 GrcsBackend TaskStageService.GetEventsSince）；
/// 2. 新事件按自增 Id 水位合并进内存缓存（上限 MaxCache 条），FINISHED 任务号同步进 FinishedTaskIds；
/// 3. 合并后触发 Changed：WaitFinishedAsync 的等待者据此唤醒，订阅的页面据此刷新 UI。
/// 消费方全部零 HTTP 读缓存，WCS 后端每秒最多只收到 1 个请求。
///
/// ── 自适应节拍 ──
/// 有等待者（两段式流程在等段1 FINISHED）时 1 秒/轮，感知延迟与旧行为一致；
/// 空闲时降到 3 秒/轮，展示场景足够，进一步给后端减负。
///
/// ── 健壮性 ──
/// WCS 后端事件是内存态，重启后事件 Id 从 1 重新开始（Id 回绕）。连续 15 轮增量无结果、
/// 或每 60 轮周期，强制全量对账一次；若发现服务端最大 Id 小于本地水位，判定后端重启，
/// 整表替换，防止缓存永远停在旧数据上。
/// 前端删除事件（任务看板/信号交互的删除按钮）后必须调用 RemoveTask/ClearAll 同步本地缓存，
/// 否则等待者会基于已删除的 FINISHED 事件误判任务完成、提前下发段2。
/// </summary>
public class TaskStageHub : IDisposable
{
    private const string WcsUrlKey = "grcs_wcs_url";
    private const string DefaultWcsUrl = "http://localhost:8230";
    private const int MaxCache = 1000;          // 前端缓存上限（足够覆盖活跃任务）
    private const int ActiveIntervalMs = 1000;  // 有等待者时的轮询间隔
    private const int IdleIntervalMs = 3000;    // 无等待者时的轮询间隔
    private const int FullRefreshEveryTicks = 60;   // 每 60 轮强制全量对账（防后端重启后 Id 回绕）
    private const int EmptyTicksBeforeFull = 15;    // 连续 15 轮无增量也触发全量（后端重启/清空检测）

    private static readonly JsonSerializerOptions Opts = new() { PropertyNameCaseInsensitive = true };

    private readonly IWcsService _wcs;
    private readonly LocalStoreService _store;
    private CancellationTokenSource? _cts;
    private bool _started;

    private List<StageChangeEvent> _events = [];
    private readonly HashSet<string> _finished = new(StringComparer.OrdinalIgnoreCase);
    private long _lastId;
    private int _waiters;
    private int _ticksSinceFull;
    private int _emptyTicks;

    /// <summary>事件缓存（按 Id 正序，最多 MaxCache 条）。</summary>
    public IReadOnlyList<StageChangeEvent> Events => _events;

    /// <summary>已 FINISHED 的任务号集合（大小写不敏感）。</summary>
    public HashSet<string> FinishedTaskIds => _finished;

    /// <summary>最近一次轮询是否成功（false = 后端连不上）。</summary>
    public bool IsOnline { get; private set; } = true;

    /// <summary>新事件合并入缓存时触发（订阅者据此刷新 UI / 唤醒等待者）。</summary>
    public event Action? Changed;

    public TaskStageHub(IWcsService wcs, LocalStoreService store)
    {
        _wcs = wcs;
        _store = store;
    }

    /// <summary>启动后台轮询（幂等，首次调用时拉一次全量）。</summary>
    public async Task EnsureStartedAsync()
    {
        if (_started) return;
        _started = true;
        _cts = new CancellationTokenSource();
        await PollOnceAsync();
        _ = LoopAsync(_cts.Token);
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var delay = Volatile.Read(ref _waiters) > 0 ? ActiveIntervalMs : IdleIntervalMs;
            try { await Task.Delay(delay, ct); }
            catch { return; }
            try { await PollOnceAsync(); }
            catch { /* 单轮失败下轮重试 */ }
        }
    }

    /// <summary>
    /// 拉一轮。三种分支：
    /// 1. 增量（默认）：?sinceId=本地水位，只收 Id 更大的新事件；
    /// 2. 周期对账（每 60 轮）：全量拉取后按 Id 水位去重合并；
    /// 3. 重启恢复（连续 15 轮增量空手、或对账发现服务端最大 Id &lt; 本地水位）：
    ///    后端事件 Id 已回绕，清空本地缓存整表替换。
    /// </summary>
    private async Task PollOnceAsync()
    {
        _ticksSinceFull++;
        var useIncremental = _lastId > 0
            && _ticksSinceFull < FullRefreshEveryTicks
            && _emptyTicks < EmptyTicksBeforeFull;

        var (ok, _, json) = await _wcs.GetTaskStageEventsAsync(ResolveBaseUrl(), useIncremental ? _lastId : 0);
        if (!ok || string.IsNullOrEmpty(json)) { IsOnline = false; _emptyTicks++; return; }
        IsOnline = true;

        List<StageChangeEvent>? fetched = null;
        try { fetched = JsonSerializer.Deserialize<List<StageChangeEvent>>(json, Opts); }
        catch { }
        if (fetched is not { Count: > 0 }) { _emptyTicks++; return; }

        var maxFetched = fetched.Max(e => e.Id);

        if (useIncremental)
        {
            // 正常增量：只收 Id 大于水位的
            var fresh = fetched.Where(e => e.Id > _lastId).ToList();
            if (fresh.Count == 0) { _emptyTicks++; return; }
            _emptyTicks = 0;
            Merge(fresh);
            _lastId = Math.Max(_lastId, fresh.Max(e => e.Id));
        }
        else
        {
            if (maxFetched < _lastId)
            {
                // 后端重启/清空导致 Id 回绕：整表替换
                _events.Clear();
                _finished.Clear();
                Merge(fetched);
            }
            else
            {
                // 周期对账：与缓存合并（去重靠 Id 水位）
                Merge(fetched.Where(e => e.Id > _lastId).ToList());
            }
            _lastId = maxFetched;
            _ticksSinceFull = 0;
            _emptyTicks = 0;
        }
    }

    private void Merge(List<StageChangeEvent> fresh)
    {
        if (fresh.Count == 0) return;
        _events.AddRange(fresh);
        if (_events.Count > MaxCache)
            _events.RemoveRange(0, _events.Count - MaxCache);
        foreach (var e in fresh)
            if (string.Equals(e.Stage, "FINISHED", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(e.TaskId))
                _finished.Add(e.TaskId);
        Changed?.Invoke();
    }

    private string ResolveBaseUrl()
    {
        var url = _store[WcsUrlKey];
        return string.IsNullOrEmpty(url) || url == "null" ? DefaultWcsUrl : url;
    }

    /// <summary>
    /// 等待任务到达 FINISHED 阶段（替代旧的"每个等待者各自 1s 轮询"实现）。
    /// 订阅 Changed 事件，任务完成即唤醒；订阅前/后各检查一次缓存，防竞态丢事件。
    /// 等待期间把 _waiters 计数 +1，让轮询节拍自动提到 1s。
    /// 无超时——等待多久由业务决定（用户明确不加超时）。
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

        Interlocked.Increment(ref _waiters);
        Changed += Handler;
        try
        {
            if (_finished.Contains(taskId)) return;
            await tcs.Task;
        }
        finally
        {
            Interlocked.Decrement(ref _waiters);
            Changed -= Handler;
        }
    }

    /// <summary>后端删除某任务的事件后调用：同步清掉本地缓存，防止等待者基于陈旧数据误判。</summary>
    public void RemoveTask(string taskId)
    {
        _events.RemoveAll(e => string.Equals(e.TaskId, taskId, StringComparison.OrdinalIgnoreCase));
        _finished.Remove(taskId);
        Changed?.Invoke();
    }

    /// <summary>后端清空全部事件后调用：本地缓存同步清空（后端 Id 单调不回绕，水位保留）。</summary>
    public void ClearAll()
    {
        _events.Clear();
        _finished.Clear();
        Changed?.Invoke();
    }

    /// <summary>手动强制全量刷新（清空全部事件后调用，立即对账）。</summary>
    public async Task RefreshNowAsync()
    {
        _ticksSinceFull = FullRefreshEveryTicks;  // 下一轮强制走全量
        await PollOnceAsync();
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }
}
