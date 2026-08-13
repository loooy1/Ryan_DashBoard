using System.Text.Json;
using GRCS.Dashboard.Modules.WcsSimulator.Extensions;
using GRCS.Dashboard.Modules.WcsSimulator.Models;
using Microsoft.JSInterop;

namespace GRCS.Dashboard.Modules.WcsSimulator.Services;

/// <summary>
/// 信号交互自动确认服务（跨页面导航存活）。
/// 采用"唯一 leader"模式：第一个启动的标签页成为 leader，负责所有 HTTP 轮询；
/// 其他标签页通过 storage 事件感知 leader 的动作，仅在 UI 需要时触发刷新。
/// 跨标签页通过 localStorage 共享状态（开关、已确认任务），storage 事件用于跨标签通知。
/// </summary>
public class SignalAutoService
{
    private const string WcsUrlKey = "grcs_wcs_url";
    private const string DefaultWcsUrl = "http://localhost:8230";
    private const string LeaderKey = "grcs_signal_auto_leader";
    private readonly IJSRuntime _js;
    private readonly IWcsService _wcs;
    private readonly CargoCodeService _cargoCodes;
    private readonly LocalStoreService _store;
    private readonly TaskLedgerService _ledger;
    private readonly EventAggregator _events;
    private static readonly JsonSerializerOptions Opts = new() { PropertyNameCaseInsensitive = true };

    /// <summary>三个自动开关（与信号交互页按钮同源：切换时写 localStorage，页面显示同一值）。</summary>
    public bool ArrivalAuto { get; private set; }
    public bool RemovalAuto { get; private set; }
    public bool AutoSend { get; private set; }
    public bool Running { get; private set; }
    public bool IsLeader { get; private set; }
    /// <summary>轮询间隔（秒），页面输入框可直接绑定。</summary>
    public int PollSeconds { get; set; } = 3;

    /// <summary>状态变化（开关切换、自动确认动作）通知，页面订阅后刷新 UI。</summary>
    public event Action? Changed;

    private CancellationTokenSource? _cts;
    private string _baseUrl = "http://localhost:8224";
    private string _wcsBaseUrl = DefaultWcsUrl;
    private List<MapStationLite> _mapStations = [];
    private bool _jsBridgeInstalled;

    public SignalAutoService(IJSRuntime js, IWcsService wcs, CargoCodeService cargoCodes,
        LocalStoreService store, TaskLedgerService ledger, EventAggregator events)
    {
        _js = js;
        _wcs = wcs;
        _cargoCodes = cargoCodes;
        _store = store;
        _ledger = ledger;
        _events = events;
    }

    /// <summary>启动后台轮询：leader 订阅 storage 事件 + 启动定时器；非 leader 仅订阅 storage 事件。</summary>
    public async Task EnsureStartedAsync()
    {
        if (Running) return;
        IsLeader = await ClaimLeaderAsync();
        LoadConfig();
        Running = true;
        _cts = new CancellationTokenSource();

        if (IsLeader)
        {
            InstallStorageBridge();
            _ = LoopAsync(_cts.Token);
        }
        else
        {
            InstallStorageBridge();
        }
    }

    private void InstallStorageBridge()
    {
        if (_jsBridgeInstalled) return;
        _jsBridgeInstalled = true;
        // 监听 localStorage 变更：当其他标签页修改确认集合等 key 时，通知当前进程内订阅者刷新 UI
        try
        {
            _js.InvokeVoidAsync("grcsRegisterStorageBridge", "grcs_signal_auto");
        }
        catch { }
    }

    /// <summary>尝试获取 leader 锁：通过 localStorage 原子写入实现，后写入的覆盖前者。</summary>
    private async Task<bool> ClaimLeaderAsync()
    {
        try
        {
            await _js.InvokeVoidAsync("grcsStoreSave", LeaderKey, "1");
            var self = await _js.InvokeAsync<string?>("grcsStoreLoad", LeaderKey);
            return self == "1";
        }
        catch { return false; }
    }

    public async Task ToggleArrivalAsync()
    {
        ArrivalAuto = !ArrivalAuto;
        await SaveFlagAsync("grcs_arrival_auto", ArrivalAuto);
        _events.Publish("grcs_arrival_auto_changed");
        Changed?.Invoke();
        if (ArrivalAuto && IsLeader) await TickArrivalAsync();
    }

    public async Task ToggleRemovalAsync()
    {
        RemovalAuto = !RemovalAuto;
        await SaveFlagAsync("grcs_removal_auto", RemovalAuto);
        _events.Publish("grcs_removal_auto_changed");
        Changed?.Invoke();
        if (RemovalAuto && IsLeader) await TickRemovalAsync();
    }

    public async Task ToggleSortingAsync()
    {
        AutoSend = !AutoSend;
        await SaveFlagAsync("grcs_ss_auto", AutoSend);
        _events.Publish("grcs_ss_auto_changed");
        Changed?.Invoke();
        if (AutoSend && IsLeader) await TickSortingAsync();
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try { await TickAsync(); }
            catch (Exception ex) { Console.WriteLine($"[SignalAuto] TickAsync 异常：{ex.Message}"); }
            try { await Task.Delay(Math.Max(1, PollSeconds) * 1000, ct); }
            catch { return; }
        }
    }

    private async Task TickAsync()
    {
        if (!ArrivalAuto && !RemovalAuto && !AutoSend) return;
        RefreshConfig();
        var (events, finished) = await FetchStageEventsAsync();
        if (ArrivalAuto) await TickArrivalCoreAsync(finished);
        if (RemovalAuto) await TickRemovalCoreAsync(finished);
        if (AutoSend) await TickSortingCoreAsync(events);
    }

    /// <summary>拉取阶段事件并提取 FINISHED 任务集合。</summary>
    private async Task<(List<StageChangeEvent> Events, HashSet<string> Finished)> FetchStageEventsAsync()
    {
        var events = new List<StageChangeEvent>();
        var finished = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var (ok, _, json) = await _wcs.GetTaskStageEventsAsync(_wcsBaseUrl);
            if (ok && !string.IsNullOrEmpty(json))
            {
                events = JsonSerializer.Deserialize<List<StageChangeEvent>>(json, Opts) ?? [];
                foreach (var e in events)
                    if (string.Equals(e.Stage, "FINISHED", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(e.TaskId))
                        finished.Add(e.TaskId);
            }
        }
        catch { }
        return (events, finished);
    }

    // ── 货物到达：自动段1（空托入库 FINISHED）或 手动段2（带载入库）→ container_ready ──
    private async Task TickArrivalCoreAsync(HashSet<string> finished)
    {
        try
        {
            if (_mapStations.Count == 0) return;
            var tasks = await _ledger.GetAsync();
            var confirmed = LoadSet("grcs_arrival_confirmed");
            var dispatched = new HashSet<string>(tasks.Where(t => t.Ok && !string.IsNullOrEmpty(t.TaskId)).Select(t => t.TaskId), StringComparer.OrdinalIgnoreCase);
            foreach (var t in tasks)
            {
                if (string.IsNullOrEmpty(t.TaskId) || confirmed.Contains(t.TaskId)) continue;
                var isAuto = t.TaskId.StartsWith("SimAuto_", StringComparison.OrdinalIgnoreCase);
                var isManual = t.TaskId.StartsWith("SimManual_", StringComparison.OrdinalIgnoreCase);
                if (!isAuto && !isManual) continue;
                if (isAuto && t.TaskType != "CONTAINER_CARRY_INBOUND") continue;
                if (isManual && t.TaskType != "CARGO_CARRY_INBOUND") continue;
                if (isAuto && (!finished.Contains(t.TaskId) || !dispatched.Contains(Seg2Id(t.TaskId)))) continue;
                var st = isAuto ? t.StationCode.LastOrDefault() : t.StationCode.FirstOrDefault();
                var cargoCode = isAuto ? await _cargoCodes.EnsureAsync(t.TaskId) : t.CargoCode;
                var (ok, _, _) = await _wcs.SendContainerReadyAsync(_baseUrl, "1.0",
                    new WcsContainerReadyRequest { TaskId = t.TaskId, StationCode = (st ?? "").ToWcsCode(_mapStations), ContainerCode = cargoCode, Warehouse = t.Warehouse });
                if (ok) { confirmed.Add(t.TaskId); await SaveSetAsync("grcs_arrival_confirmed", confirmed); _events.Publish("grcs_arrival_confirmed_changed"); Changed?.Invoke(); }
            }
        }
        catch (Exception ex) { Console.WriteLine($"[SignalAuto] 到达异常：{ex.Message}"); }
    }

    // ── 货物移除：自动段1（带载出库 FINISHED）或 手动段2（空托回库）→ container_remove ──
    private async Task TickRemovalCoreAsync(HashSet<string> finished)
    {
        try
        {
            if (_mapStations.Count == 0) return;
            var tasks = await _ledger.GetAsync();
            var confirmed = LoadSet("grcs_removal_confirmed");
            var dispatched = new HashSet<string>(tasks.Where(t => t.Ok && !string.IsNullOrEmpty(t.TaskId)).Select(t => t.TaskId), StringComparer.OrdinalIgnoreCase);
            foreach (var t in tasks)
            {
                if (string.IsNullOrEmpty(t.TaskId) || confirmed.Contains(t.TaskId)) continue;
                var isAuto = t.TaskId.StartsWith("SimAuto_", StringComparison.OrdinalIgnoreCase);
                var isManual = t.TaskId.StartsWith("SimManual_", StringComparison.OrdinalIgnoreCase);
                if (!isAuto && !isManual) continue;
                if (isAuto && t.TaskType != "CARGO_CARRY_OUTBOUND") continue;
                if (isManual && t.TaskType != "CONTAINER_CARRY_OUTBOUND") continue;
                if (isAuto && (!finished.Contains(t.TaskId) || !dispatched.Contains(Seg2Id(t.TaskId)))) continue;
                var st = isAuto ? t.StationCode.LastOrDefault() : t.StationCode.FirstOrDefault();
                var containerCode = string.IsNullOrEmpty(t.CargoCode) ? t.ContainerCode : t.CargoCode;
                var (ok, _, _) = await _wcs.SendContainerRemoveAsync(_baseUrl, "1.0",
                    new WcsContainerRemoveRequest { StationCode = (st ?? "").ToWcsCode(_mapStations), ContainerCode = containerCode, Warehouse = t.Warehouse });
                if (ok) { confirmed.Add(t.TaskId); await SaveSetAsync("grcs_removal_confirmed", confirmed); _events.Publish("grcs_removal_confirmed_changed"); Changed?.Invoke(); }
            }
        }
        catch (Exception ex) { Console.WriteLine($"[SignalAuto] 移除异常：{ex.Message}"); }
    }

    // ── 分拣完成：FINISHED 且站点是分拣台 → container_operation_finish ──
    private async Task TickSortingCoreAsync(List<StageChangeEvent> events)
    {
        try
        {
            if (_mapStations.Count == 0) return;
            var finishedList = events.Where(e => e.Stage == "FINISHED").OrderBy(e => e.Time).ToList();
            if (finishedList.Count == 0) return;
            var sent = LoadSet("grcs_ss_sent");
            foreach (var e in finishedList)
            {
                if (sent.Contains(e.TaskId)) continue;
                var station = _mapStations.FirstOrDefault(s => s.Mark == e.StationCode)
                    ?? _mapStations.FirstOrDefault(s => s.Mark == e.StationCode.ToMark());
                if (station == null) continue;
                if ((station.StationType & (MapStationTypeBits.PickingStation | MapStationTypeBits.PeopleStation)) == 0)
                    continue;
                var sendTaskId = e.TaskId + "_R";
                var (ok2, code2, _) = await _wcs.SendOperationFinishAsync(_baseUrl, "1.0",
                    new WcsOperationFinishRequest { TaskId = sendTaskId, ContainerCode = e.ContainerCode, RemoveContainer = false, StationCode = "", AreaCode = "", Warehouse = e.Warehouse });
                if (!ok2) { Console.WriteLine($"[SignalAuto] 分拣 {sendTaskId} 发送失败 HTTP {code2}"); continue; }
                sent.Add(e.TaskId);
                await SaveSetAsync("grcs_ss_sent", sent);
                _events.Publish("grcs_ss_sent_changed");
                Changed?.Invoke();
            }
        }
        catch (Exception ex) { Console.WriteLine($"[SignalAuto] 分拣异常：{ex.Message}"); }
    }

    private async Task TickArrivalAsync()
    {
        var (_, finished) = await FetchStageEventsAsync();
        await TickArrivalCoreAsync(finished);
    }
    private async Task TickRemovalAsync()
    {
        var (_, finished) = await FetchStageEventsAsync();
        await TickRemovalCoreAsync(finished);
    }
    private async Task TickSortingAsync()
    {
        var (events, _) = await FetchStageEventsAsync();
        await TickSortingCoreAsync(events);
    }

    private static string Seg2Id(string seg1Id)
    {
        if (seg1Id.StartsWith("SimAuto_", StringComparison.OrdinalIgnoreCase) && seg1Id.Length >= 2 && seg1Id[^1] == 'a')
            return seg1Id[..^1] + "b";
        return seg1Id;
    }

    private void LoadConfig()
    {
        try
        {
            var a = _store["grcs_arrival_auto"]; ArrivalAuto = bool.TryParse(a, out var av) && av;
            var r = _store["grcs_removal_auto"]; RemovalAuto = bool.TryParse(r, out var rv) && rv;
            var s = _store["grcs_ss_auto"]; AutoSend = bool.TryParse(s, out var sv) && sv;
            RefreshConfig();
        }
        catch { }
    }

    private void RefreshConfig()
    {
        try
        {
            var grcs = _store["grcs_grcs_url"];
            if (!string.IsNullOrEmpty(grcs) && grcs != "null") _baseUrl = grcs;
            var wcs = _store[WcsUrlKey];
            if (!string.IsNullOrEmpty(wcs) && wcs != "null") _wcsBaseUrl = wcs;
            var json = _store["grcs_map_stations"];
            if (!string.IsNullOrEmpty(json) && json != "null")
            {
                var cache = JsonSerializer.Deserialize<MapStationCache>(json, Opts);
                _mapStations = cache?.Stations ?? [];
            }
        }
        catch { }
    }

    private HashSet<string> LoadSet(string key)
    {
        try
        {
            var s = _store[key];
            if (!string.IsNullOrEmpty(s) && s != "null")
                return JsonSerializer.Deserialize<HashSet<string>>(s, Opts) ?? [];
        }
        catch { }
        return [];
    }

    private async Task SaveSetAsync(string key, HashSet<string> set)
    {
        await _store.SetAsync(_js, key, JsonSerializer.Serialize(set));
    }

    private async Task SaveFlagAsync(string key, bool value)
    {
        await _store.SetAsync(_js, key, value.ToString().ToLowerInvariant());
    }
}
