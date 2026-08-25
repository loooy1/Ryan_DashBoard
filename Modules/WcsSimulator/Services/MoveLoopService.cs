using GRCS.Dashboard.Modules.WcsSimulator.Models;
using GRCS.Dashboard.Modules.WcsSimulator.Models.TWD;

namespace GRCS.Dashboard.Modules.WcsSimulator.Services;

/// <summary>
/// 纯移动任务循环服务（scoped：跨页面导航存活，离开「自动化任务」页任务继续下发）。
/// 固定节拍下发 MOVE_ONLY：每 Interval 秒整点发一个，dispatch 限时 8 秒（超时按失败记），
/// 耗时不拖累节奏；落后不堆积。租约（move/start + 5s 心跳）与停止（move/stop）由本服务持有。
/// 页面仅作为视图：订阅 Changed 事件刷新 UI（日志/统计/运行状态）。
/// </summary>
public class MoveLoopService
{
    public class Options
    {
        public string TabId { get; set; } = "";
        public string SceneName { get; set; } = "";
        public string OrderIdPrefix { get; set; } = "SimMoveOnly";
        public int Priority { get; set; } = 50;
        public int Interval { get; set; } = 3;
        public List<MapStationLite> Pool { get; set; } = [];
        public bool RangeEnabled { get; set; }
    }

    private readonly WcsApiClient _api;
    private CancellationTokenSource? _cts;
    private Options? _opts;

    public bool Running { get; private set; }
    public int Seq { get; private set; }
    public int Total { get; private set; }
    public int Ok { get; private set; }
    public int Fail { get; private set; }
    public int Interval => _opts?.Interval ?? 3;

    /// <summary>状态变化（循环线程触发，页面订阅后 InvokeAsync(StateHasChanged)）。</summary>
    public event Action? Changed;

    public MoveLoopService(WcsApiClient api) => _api = api;

    /// <summary>启动循环：登记租约成功后开始固定节拍下发 + 心跳续约。</summary>
    public async Task<(bool Ok, string? Reason)> StartAsync(Options opts)
    {
        if (Running) return (false, "移动循环已在运行");
        var lease = await _api.PostAsync<object, MoveLeaseResult>("/api/wcs/auto/move/start", new { tabId = opts.TabId });
        if (lease is not { Success: true })
            return (false, lease?.Reason ?? "后端拒绝（互斥：自动化模板或另一标签页正在下发）");
        _opts = opts;
        Running = true;
        Seq = 0; Total = 0; Ok = 0; Fail = 0;
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        _ = DispatchLoopAsync(_cts.Token);
        _ = HeartbeatAsync(_cts.Token);
        return (true, null);
    }

    /// <summary>停止循环：取消循环与心跳，释放租约，统计清零。</summary>
    public void Stop()
    {
        if (!Running) return;
        Running = false;
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        Total = 0; Ok = 0; Fail = 0;
        var tabId = _opts?.TabId ?? "";
        if (!string.IsNullOrEmpty(tabId)) _ = _api.PostAsync("/api/wcs/auto/move/stop", new { tabId });
        Notify();
    }

    private void Notify() => Changed?.Invoke();

    private async Task HeartbeatAsync(CancellationToken ct)
    {
        var tabId = _opts?.TabId ?? "";
        while (Running && !ct.IsCancellationRequested)
        {
            try { await Task.Delay(5000, ct); } catch { return; }
            if (!Running) break;
            _ = _api.PostAsync("/api/wcs/auto/move/beat", new { tabId });
        }
    }

    private async Task DispatchLoopAsync(CancellationToken ct)
    {
        var opts = _opts;
        if (opts == null) return;
        // 固定节拍：每 interval 秒整点下发一次（dispatch 限时 8 秒，超时按失败处理），
        // dispatch 耗时不会拖累下一轮节奏；落后时不堆积，从当前时刻重新起算
        var nextTick = System.Diagnostics.Stopwatch.GetTimestamp();
        while (Running && !ct.IsCancellationRequested)
        {
            var pool = opts.Pool;
            if (pool.Count == 0)
            {
                Running = false;
                ct.ThrowIfCancellationRequested();
                var tabId = opts.TabId;
                if (!string.IsNullOrEmpty(tabId)) _ = _api.PostAsync("/api/wcs/auto/move/stop", new { tabId });
                Total = 0; Ok = 0; Fail = 0;
                Notify();
                return;
            }

            var st = pool[Random.Shared.Next(pool.Count)];
            var payload = new VehicleOrderRequest
            {
                CreateTime = DateTime.Now,
                SceneName = opts.SceneName,
                OrderType = "MOVE_ONLY",
                OrderId = $"{opts.OrderIdPrefix}_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString("x").ToUpper()}_{++Seq}",
                OrderName = "wcs模拟器纯移动任务",
                VehicleName = null,
                Priority = opts.Priority,
                StationCodes = [st.Mark],
            };

            var sw = System.Diagnostics.Stopwatch.StartNew();
            MoveDispatchResult? resp = null;
            try
            {
                using var dts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                dts.CancelAfter(8000);   // dispatch 限时 8 秒：超时不拖累下一轮节奏
                resp = await _api.PostAsync<object, MoveDispatchResult>("/api/wcs/auto/move/dispatch", payload, dts.Token);
            }
            catch (OperationCanceledException) { }
            sw.Stop();
            var ok = resp?.Success == true;
            Total++;
            if (ok) Ok++; else Fail++;
            Notify();

            if (!Running || ct.IsCancellationRequested) break;
            // 对齐节拍：dispatch 慢也不拖累下一轮（落后则从当前重新起算，不堆积）
            var now = System.Diagnostics.Stopwatch.GetTimestamp();
            var intervalTicks = (long)opts.Interval * 1000 * System.Diagnostics.Stopwatch.Frequency / 1000;
            if (now >= nextTick) nextTick = now + intervalTicks;
            var remainMs = (int)((nextTick - now) * 1000 / System.Diagnostics.Stopwatch.Frequency);
            if (remainMs > 0) { try { await Task.Delay(remainMs, ct); } catch { break; } }
            nextTick += intervalTicks;
        }
        if (Running)
        {
            Running = false;
            Total = 0; Ok = 0; Fail = 0;
            Notify();
        }
    }
}