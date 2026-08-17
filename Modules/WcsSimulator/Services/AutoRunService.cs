using System.Text.Json;
using GRCS.Dashboard.Modules.WcsSimulator.Extensions;
using GRCS.Dashboard.Modules.WcsSimulator.Models;
using Microsoft.JSInterop;

namespace GRCS.Dashboard.Modules.WcsSimulator.Services;

/// <summary>
/// 自动化轮询运行状态（跨页面导航存活）：轮询储位内的空托/带货托，自动下发 入库/出库/分拣 任务。
/// Blazor WASM 的 scoped DI 等效单例，组件销毁不重置运行状态与日志；手动停止后日志保留，可手动清空。
/// </summary>
public class AutoRunService
{
    private const string WcsUrlKey = "grcs_wcs_url";
    private const string DefaultWcsUrl = "http://localhost:8230";
    private readonly IWcsService _wcs;
    private readonly CargoCodeService _cargoCodes;
    private readonly StationLockService _stationLocks;
    private readonly SignalAutoService _signalAuto;
    private readonly TaskStageHub _stageHub;
    private static readonly JsonSerializerOptions Opts = new() { PropertyNameCaseInsensitive = true };

    public bool Running { get; private set; }
    public int Interval { get; set; } = 5;
    public int FlowMode { get; set; }              // 带货托任务：0=出库/分拣交替，1=只分拣，2=只出库，3=无任务（只空托入库）
    public int Dispatched { get; private set; }
    public List<AutoLogEntry> Logs { get; } = [];

    /// <summary>状态变化通知（日志追加、启停等），页面订阅后刷新 UI。</summary>
    public event Action? Changed;

    private readonly HashSet<string> _handled = []; // 处理中/已处理托盘码：发现即占用，流程结束才解除（覆盖搬运途中，防重复下发）
    private int _seq;                               // 自动化任务序号（TaskId 唯一性）
    private int _outboundTurn;                      // 带货托 出库/分拣 交替计数
    private string _baseUrl = "http://localhost:8224";
    private string _wcsBaseUrl = DefaultWcsUrl;
    private string _sceneName = "";
    private List<MapStationLite> _mapStations = [];
    private AutoRangeConfig _range = new();
    private readonly LocalStoreService _store;
    private readonly TaskLedgerService _ledger;

    public AutoRunService(IWcsService wcs, CargoCodeService cargoCodes, StationLockService stationLocks, SignalAutoService signalAuto, TaskStageHub stageHub, LocalStoreService store, TaskLedgerService ledger)
    {
        _wcs = wcs;
        _cargoCodes = cargoCodes;
        _stationLocks = stationLocks;
        _signalAuto = signalAuto;
        _stageHub = stageHub;
        _store = store;
        _ledger = ledger;
    }

    public async Task ToggleAsync()
    {
        if (Running) { Stop(); return; }
        await StartAsync();
    }

    public void Stop()
    {
        Running = false;
        Log("⏹ 自动化已停止", "#fbbf24");
    }

    public async Task StartAsync()
    {
        if (Running) return;
        LoadConfig();
        if (string.IsNullOrEmpty(_sceneName)) { Log("❌ 请先填写场景名称（连接设置）", "#f87171"); return; }
        if (_mapStations.Count == 0) { Log("❌ 请先在「地图信息」页读取 map.json", "#f87171"); return; }
        // 信号自动确认服务一并拉起（后台跨页面运行）：自动化流程的 container_ready/remove 信号
        // 不依赖信号交互页在场，GRCS 的段2 不会被信号缺失阻塞
        await _signalAuto.EnsureStartedAsync();
        Running = true;
        Log("▶ 自动化启动，轮询间隔 " + Interval + " 秒", "#4ade80");
        _ = PollLoopAsync();
    }

    public void ClearLogs() { Logs.Clear(); Dispatched = 0; Changed?.Invoke(); }

    private void LoadConfig()
    {
        try
        {
            var wh = _store["grcs_warehouse"];
            if (!string.IsNullOrEmpty(wh) && wh != "null") _sceneName = wh;
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
            LoadRangeConfig();
        }
        catch { }
    }

    /// <summary>加载选点范围限制（grcs_auto_range），限定接驳位/储位/分拣台候选池。</summary>
    private void LoadRangeConfig()
    {
        try
        {
            var r = _store["grcs_auto_range"];
            if (!string.IsNullOrEmpty(r) && r != "null")
                _range = JsonSerializer.Deserialize<AutoRangeConfig>(r, Opts) ?? new AutoRangeConfig();
        }
        catch { _range = new AutoRangeConfig(); }
    }

    /// <summary>在站点池上应用选点范围限制：储位/接驳位/分拣台候选池全部收窄到限定范围。</summary>
    private void ApplyRange(List<MapStationLite> storages, List<MapStationLite> transferPoints, List<MapStationLite> pickingStations)
    {
        if (!_range.Enabled) return;
        var pool = _range.ApplyTo(_mapStations);
        storages.RemoveAll(s => !pool.Contains(s));
        transferPoints.RemoveAll(s => !pool.Contains(s));
        pickingStations.RemoveAll(s => !pool.Contains(s));
    }

    private async Task PollLoopAsync()
    {
        while (Running)
        {
            try { await PollOnceAsync(); }
            catch (Exception ex) { Log("❌ 轮询异常: " + ex.Message, "#f87171"); }
            if (Running) await Task.Delay(Interval * 1000);
        }
    }

    private async Task PollOnceAsync()
    {
        LoadRangeConfig(); // 每轮刷新选点范围（运行中改范围下轮生效）

        // 1. 查库存
        var (ok, httpCode, json) = await _wcs.QueryCargoInventoryAsync(_baseUrl, scene: _sceneName);
        if (!ok || string.IsNullOrEmpty(json))
        { Log("⚠ 库存查询失败（HTTP " + httpCode + "），等待下轮", "#fbbf24"); return; }
        var result = JsonSerializer.Deserialize<CargoQueryResult>(json, Opts);
        var records = result?.Data?.Records ?? [];

        // 3. 站点池（先建池并应用选点范围，再用范围储位集做库存判定，保证范围外托盘不被下发）
        var storages = _mapStations.Where(s => s.StaEnable && (s.StationType & MapStationTypeBits.StorageLocation) != 0).ToList();
        var transferPoints = _mapStations.Where(s => s.StaEnable && (s.StationType & MapStationTypeBits.TransferPoint) != 0).ToList();
        var pickingStations = _mapStations.Where(s => s.StaEnable && (s.StationType & MapStationTypeBits.PeopleStation) != 0).ToList();
        // 选点范围限制：用户可在自动化任务页限定接驳位/储位/分拣台候选范围
        ApplyRange(storages, transferPoints, pickingStations);

        // 2. 配对分析：只认（范围内）储位内的空托/带货托（接驳位、分拣台等非储位站点上的容器不参与，
        //    否则段1 搬到接驳位等装货的托盘会被误判为空托而重复下发入库）
        var storageMarks = new HashSet<string>(storages.Select(s => s.Mark));
        var pallets = new List<(string Code, string Station)>();
        var cargos = new List<(string Code, string Station)>();
        int lockedCnt = 0, loadedCnt = 0;
        foreach (var c in records)
        {
            if (c.IsLocked) { lockedCnt++; continue; }
            if (c.IsLoaded) { loadedCnt++; continue; }
            if (string.IsNullOrEmpty(c.Code) || string.IsNullOrEmpty(c.CurrentStationCode)) continue;
            // 库存站点码带 _0/_1 后缀，用扩展示方法归一化到地图 Mark 再匹配
            var stationCode = c.CurrentStationCode.ToMark();
            if (!storageMarks.Contains(stationCode)) continue; // 非储位站点（接驳位/分拣台等）跳过
            if (c.IsPallet()) pallets.Add((c.Code, stationCode));
            else if (c.IsCargo()) cargos.Add((c.Code, stationCode));
        }
        var cargoMarks = new HashSet<string>(cargos.Select(c => c.Station));
        var palletMarks = new HashSet<string>(pallets.Select(p => p.Station));
        var emptyPallets = pallets.Where(p => !cargoMarks.Contains(p.Station)).ToList();
        var loadedPallets = pallets.Where(p => cargoMarks.Contains(p.Station)).ToList();
        var pairedCargos = cargos.Where(c => palletMarks.Contains(c.Station)).ToList();

        // 3b. 终点储位锁定过滤
        var lockedStations = await _stationLocks.GetLockedAsync();
        var occupiedMarks = new HashSet<string>(pallets.Select(p => p.Station).Concat(cargos.Select(c => c.Station)));
        var emptyStorages = storages.Where(s => !occupiedMarks.Contains(s.Mark) && !lockedStations.Contains(s.Mark)).ToList();

        // 4. 下发：空托 → 入库；带货托 → 按模式（交替/只分拣/只出库）（托盘发现即占用，流程结束才解除）
        var found = 0; var busy = 0;
        foreach (var (code, loc) in emptyPallets)
        {
            if (_handled.Contains(code)) { busy++; continue; }
            _handled.Add(code);
            found++;
            _ = ProcessInboundAsync(code, loc, transferPoints, emptyStorages);
        }

        // FlowMode 3「无任务」：空托照常入库；入库产生的带货托不再触发出库/分拣，只留在储位。
        // 纯跳过（不加入 _handled），避免"无任务"模式把带货托占用而阻塞其他模式下轮恢复。
        if (FlowMode == 3)
        {
            var skipped = loadedPallets.Count;
            string msg3;
            if (found > 0)
                msg3 = "轮询完成：发现 " + found + " 个空托开始入库（带货托 " + skipped + " 个按「无任务」跳过）";
            else if (busy > 0)
                msg3 = "轮询完成：无新空托可入库（" + busy + " 个已在处理中；带货托 " + skipped + " 个按「无任务」跳过）";
            else if (skipped > 0)
                msg3 = "轮询完成：储位内无空托可入库（带货托 " + skipped + " 个按「无任务」跳过）";
            else
                msg3 = "轮询完成：储位内未发现可下发的空托" + (lockedCnt > 0 ? "（跳过锁定 " + lockedCnt + "）" : "");
            Log(msg3, found == 0 ? "#94a3b8" : "#4ade80");
            return;
        }

        foreach (var (code, loc) in loadedPallets)
        {
            if (_handled.Contains(code)) { busy++; continue; }
            var cargo = pairedCargos.FirstOrDefault(c => c.Station == loc);
            if (cargo.Code == null) { Log("⚠ 带货托盘 " + code + "@" + loc + " 无配对货物，无法下发", "#fbbf24"); continue; }
            _handled.Add(code);
            found++;
            if (FlowMode == 1)
                _ = ProcessSortingAsync(code, cargo.Code, loc, pickingStations);
            else if (FlowMode == 2)
                _ = ProcessOutboundAsync(code, cargo.Code, loc, transferPoints, emptyStorages);
            else if (_outboundTurn++ % 2 == 0)
                _ = ProcessOutboundAsync(code, cargo.Code, loc, transferPoints, emptyStorages);
            else
                _ = ProcessSortingAsync(code, cargo.Code, loc, pickingStations);
        }

        // 日志区分三种情况，避免"空托 1 / 处理中 1"被误读为矛盾
        var visible = emptyPallets.Count + loadedPallets.Count; // 库存可见托盘（可能全部已占用）
        string msg;
        if (found > 0)
            msg = "轮询完成：发现 " + found + " 个托盘开始处理（库存可见 " + visible + " 个，其中 " + busy + " 个已在处理中）";
        else if (busy > 0)
            msg = "轮询完成：无新托盘可下发（库存可见 " + visible + " 个，全部已在处理中）";
        else
            msg = "轮询完成：储位内未发现可下发的空托/带货托" + (lockedCnt + loadedCnt > 0 ? "（跳过在途 " + loadedCnt + " / 锁定 " + lockedCnt + "）" : "");
        Log(msg, found == 0 ? "#94a3b8" : "#4ade80");
    }

    // ── 空托盘入库（段1 空托 → 段2 带载，段1 完成后货物码随卡片生成） ──
    private async Task ProcessInboundAsync(string palletCode, string loc, List<MapStationLite> transferPoints, List<MapStationLite> emptyStorages)
    {
        try
        {
            if (transferPoints.Count == 0) { Log("❌ 托盘 " + palletCode + "@" + loc + " 无法下发入库：无可用接驳位", "#f87171"); Finish(palletCode); return; }
            if (emptyStorages.Count == 0) { Log("❌ 托盘 " + palletCode + "@" + loc + " 无法下发入库：无空储位（被锁定或已占用）", "#f87171"); Finish(palletCode); return; }
            var rand = Random.Shared;
            var tpSt = transferPoints[rand.Next(transferPoints.Count)];
            var dstSt = emptyStorages[rand.Next(emptyStorages.Count)];
            emptyStorages.Remove(dstSt);   // 终点储位同批内不重复
            var srcSt = _mapStations.FirstOrDefault(s => s.Mark == loc);
            var seq = ++_seq;
            var id1 = "SimAuto_" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString("x").ToUpper() + "_A" + seq + "a";
            var id2 = id1[..^1] + "b";
            Log("发现空托盘 " + palletCode + "@" + loc + " → 下发入库段1 " + id1 + "（储位 " + (srcSt != null ? srcSt.Mark : loc) + " → 接驳位 " + tpSt.Mark + "），段2 回储位 " + dstSt.Mark, "#60a5fa");
            // 锁定终点目标储位，流程完成（段2 FINISHED）后惰性释放
            await _stationLocks.AcquireAsync(dstSt.Mark, id2);

            var payload1 = new WcsTaskGroup
            {
                GroupId = "G_" + id1, MsgTime = DateTime.Now.ToString("O"), PriorityCode = 50, Warehouse = _sceneName,
                Tasks = [new WcsTaskItem { TaskId = id1, TaskType = "CONTAINER_CARRY_INBOUND", ContainerCode = palletCode, StationCode = [srcSt != null ? srcSt.ToWcsCode() : loc, tpSt.ToWcsCode()] }]
            };
            var (ok1, code1, j1) = await _wcs.SendTaskGroupAsync(_baseUrl, "1.0", payload1);
            await SaveLedgerAsync([new TaskLedgerEntry { TaskId = id1, TaskType = "CONTAINER_CARRY_INBOUND", ContainerCode = palletCode, CargoCode = "", StationCode = payload1.Tasks[0].StationCode, Warehouse = _sceneName, Time = DateTime.Now.ToString("O"), Ok = ok1, StatusCode = code1 }]);
            if (!ok1) { Log("❌ 入库段1 下发失败 HTTP" + code1 + "：" + palletCode, "#f87171"); Finish(palletCode); return; }
            Dispatched++;

            await WaitFinishedAsync(id1);
            Log("段1 " + id1 + " 完成，托盘 " + palletCode + " 已到接驳位", "#4ade80");

            // 货物码随到达卡片生成（与信号交互页同一存储，先读后写保证一致）
            var cargoCode = await _cargoCodes.EnsureAsync(id1);
            var payload2 = new WcsTaskGroup
            {
                GroupId = "G_" + id2, MsgTime = DateTime.Now.ToString("O"), PriorityCode = 50, Warehouse = _sceneName,
                Tasks = [new WcsTaskItem { TaskId = id2, TaskType = "CARGO_CARRY_INBOUND", ContainerCode = cargoCode, StationCode = [tpSt.ToWcsCode(), dstSt.ToWcsCode()] }]
            };
            var (ok2, code2, j2) = await _wcs.SendTaskGroupAsync(_baseUrl, "1.0", payload2);
            await SaveLedgerAsync([new TaskLedgerEntry { TaskId = id2, TaskType = "CARGO_CARRY_INBOUND", ContainerCode = palletCode, CargoCode = cargoCode, StationCode = payload2.Tasks[0].StationCode, Warehouse = _sceneName, Time = DateTime.Now.ToString("O"), Ok = ok2, StatusCode = code2 }]);
            if (ok2) { Dispatched++; Log("✓ 入库段2 " + id2 + " 已下发（货物码 " + cargoCode + "，托盘 " + palletCode + "）", "#4ade80"); }
            else Log("❌ 入库段2 下发失败 HTTP" + code2 + "（托盘 " + palletCode + "）", "#f87171");
        }
        catch (Exception ex) { Log("❌ 入库流程异常（托盘 " + palletCode + "）: " + ex.Message, "#f87171"); }
        Finish(palletCode);
    }

    // ── 带货托盘出库（段1 带载 → 段2 空托回库） ──
    private async Task ProcessOutboundAsync(string palletCode, string cargoCode, string loc, List<MapStationLite> transferPoints, List<MapStationLite> emptyStorages)
    {
        try
        {
            if (transferPoints.Count == 0) { Log("❌ 带货托盘 " + palletCode + "@" + loc + " 无法下发出库：无可用接驳位", "#f87171"); Finish(palletCode); return; }
            if (emptyStorages.Count == 0) { Log("❌ 带货托盘 " + palletCode + "@" + loc + " 无法下发出库：无空储位（被锁定或已占用）", "#f87171"); Finish(palletCode); return; }
            var rand = Random.Shared;
            var tpSt = transferPoints[rand.Next(transferPoints.Count)];
            var dstSt = emptyStorages[rand.Next(emptyStorages.Count)];
            emptyStorages.Remove(dstSt);   // 终点储位同批内不重复
            var srcSt = _mapStations.FirstOrDefault(s => s.Mark == loc);
            var seq = ++_seq;
            var id1 = "SimAuto_" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString("x").ToUpper() + "_A" + seq + "a";
            var id2 = id1[..^1] + "b";
            Log("发现带货托盘 " + palletCode + "（货 " + cargoCode + "）@" + loc + " → 下发出库段1 " + id1 + "（储位 " + (srcSt != null ? srcSt.Mark : loc) + " → 接驳位 " + tpSt.Mark + "），段2 回库储位 " + dstSt.Mark, "#60a5fa");
            await _stationLocks.AcquireAsync(dstSt.Mark, id2);

            var payload1 = new WcsTaskGroup
            {
                GroupId = "G_" + id1, MsgTime = DateTime.Now.ToString("O"), PriorityCode = 50, Warehouse = _sceneName,
                Tasks = [new WcsTaskItem { TaskId = id1, TaskType = "CARGO_CARRY_OUTBOUND", ContainerCode = cargoCode, StationCode = [srcSt != null ? srcSt.ToWcsCode() : loc, tpSt.ToWcsCode()] }]
            };
            var (ok1, code1, j1) = await _wcs.SendTaskGroupAsync(_baseUrl, "1.0", payload1);
            await SaveLedgerAsync([new TaskLedgerEntry { TaskId = id1, TaskType = "CARGO_CARRY_OUTBOUND", ContainerCode = palletCode, CargoCode = cargoCode, StationCode = payload1.Tasks[0].StationCode, Warehouse = _sceneName, Time = DateTime.Now.ToString("O"), Ok = ok1, StatusCode = code1 }]);
            if (!ok1) { Log("❌ 出库段1 下发失败 HTTP" + code1 + "：" + palletCode, "#f87171"); Finish(palletCode); return; }
            Dispatched++;

            await WaitFinishedAsync(id1);
            Log("段1 " + id1 + " 完成，货物 " + cargoCode + " 已到接驳位", "#4ade80");

            var payload2 = new WcsTaskGroup
            {
                GroupId = "G_" + id2, MsgTime = DateTime.Now.ToString("O"), PriorityCode = 50, Warehouse = _sceneName,
                Tasks = [new WcsTaskItem { TaskId = id2, TaskType = "CONTAINER_CARRY_OUTBOUND", ContainerCode = palletCode, StationCode = [tpSt.ToWcsCode(), dstSt.ToWcsCode()] }]
            };
            var (ok2, code2, j2) = await _wcs.SendTaskGroupAsync(_baseUrl, "1.0", payload2);
            await SaveLedgerAsync([new TaskLedgerEntry { TaskId = id2, TaskType = "CONTAINER_CARRY_OUTBOUND", ContainerCode = palletCode, CargoCode = cargoCode, StationCode = payload2.Tasks[0].StationCode, Warehouse = _sceneName, Time = DateTime.Now.ToString("O"), Ok = ok2, StatusCode = code2 }]);
            if (ok2) { Dispatched++; Log("✓ 出库段2 " + id2 + " 已下发（空托 " + palletCode + " 回库）", "#4ade80"); }
            else Log("❌ 出库段2 下发失败 HTTP" + code2 + "（托盘 " + palletCode + "）", "#f87171");
        }
        catch (Exception ex) { Log("❌ 出库流程异常（托盘 " + palletCode + "）: " + ex.Message, "#f87171"); }
        Finish(palletCode);
    }

    // ── 带货托盘分拣（只有段1，完成即流程结束） ──
    private async Task ProcessSortingAsync(string palletCode, string cargoCode, string loc, List<MapStationLite> pickingStations)
    {
        try
        {
            if (pickingStations.Count == 0) { Log("❌ 带货托盘 " + palletCode + "@" + loc + " 无法下发分拣：无人工分拣台", "#f87171"); Finish(palletCode); return; }
            var rand = Random.Shared;
            var pickSt = pickingStations[rand.Next(pickingStations.Count)];
            var srcSt = _mapStations.FirstOrDefault(s => s.Mark == loc);
            var seq = ++_seq;
            var id1 = "SimAuto_" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString("x").ToUpper() + "_A" + seq + "a";
            Log("发现带货托盘 " + palletCode + "（货 " + cargoCode + "）@" + loc + " → 下发分拣 " + id1 + "（" + (srcSt != null ? srcSt.Mark : loc) + " → 分拣台 " + pickSt.Mark + "）", "#60a5fa");

            var payload1 = new WcsTaskGroup
            {
                GroupId = "G_" + id1, MsgTime = DateTime.Now.ToString("O"), PriorityCode = 50, Warehouse = _sceneName,
                Tasks = [new WcsTaskItem { TaskId = id1, TaskType = "SORTING", ContainerCode = cargoCode, StationCode = [srcSt != null ? srcSt.ToWcsCode() : loc, pickSt.ToWcsCode()] }]
            };
            var (ok1, code1, j1) = await _wcs.SendTaskGroupAsync(_baseUrl, "1.0", payload1);
            await SaveLedgerAsync([new TaskLedgerEntry { TaskId = id1, TaskType = "SORTING", ContainerCode = palletCode, CargoCode = cargoCode, StationCode = payload1.Tasks[0].StationCode, Warehouse = _sceneName, Time = DateTime.Now.ToString("O"), Ok = ok1, StatusCode = code1 }]);
            if (!ok1) { Log("❌ 分拣下发失败 HTTP" + code1 + "：" + palletCode, "#f87171"); Finish(palletCode); return; }
            Dispatched++;

            await WaitFinishedAsync(id1);
            Log("✓ 分拣 " + id1 + " 完成，货物 " + cargoCode + " 已到分拣台", "#4ade80");
        }
        catch (Exception ex) { Log("❌ 分拣流程异常（托盘 " + palletCode + "）: " + ex.Message, "#f87171"); }
        Finish(palletCode);
    }

    /// <summary>等待任务 FINISHED：订阅共享轮询器（全应用唯一 task-stages 轮询），不再自行 1s 打 HTTP。</summary>
    private async Task WaitFinishedAsync(string taskId)
    {
        await _stageHub.WaitFinishedAsync(taskId);
    }

    /// <summary>保存任务台账（唯一数据源，取代下发历史，内存缓存写穿）。ContainerCode=托盘号，CargoCode=货物号。</summary>
    private async Task SaveLedgerAsync(List<TaskLedgerEntry> entries) => await _ledger.AppendAsync(entries);

    /// <summary>流程结束（成功或失败）后解除托盘占用，下轮轮询可再次发现（此时库存状态已变化）。</summary>
    private void Finish(string palletCode) => _handled.Remove(palletCode);

    /// <summary>
    /// 日志通知合并（防抖）：连续日志在 150ms 内只触发一次 Changed。
    /// 一轮轮询 PollOnceAsync 会连写 5~10 条日志（发现托盘 → 下发段1 → 台账 → 完成……），
    /// 优化前每条都 Changed?.Invoke()，底部状态栏和「自动化任务」页的 500 条日志列表
    /// 每轮被重渲染 5~10 次。合并后每轮最多 1 次；单独的日志（如后台流程完成时的一条）
    /// 150ms 后也能及时送达 UI，不会积压。
    /// </summary>
    private bool _notifyPending;
    private System.Threading.Timer? _flushTimer;

    private void Log(string msg, string color = "#94a3b8")
    {
        Logs.Add(new AutoLogEntry { Time = DateTime.Now.ToString("HH:mm:ss"), Message = msg, Color = color });
        if (Logs.Count > 500) Logs.RemoveRange(0, Logs.Count - 500);
        _notifyPending = true;
        _flushTimer?.Dispose();
        _flushTimer = new System.Threading.Timer(_ =>
        {
            _flushTimer?.Dispose();
            _flushTimer = null;
            if (_notifyPending)
            {
                _notifyPending = false;
                Changed?.Invoke();
            }
        }, null, 150, Timeout.Infinite);
    }

    /// <summary>立即落盘一次未发出的通知（停止/清空时保证 UI 同步）。</summary>
    private void FlushNotifications()
    {
        _flushTimer?.Dispose();
        _flushTimer = null;
        if (_notifyPending)
        {
            _notifyPending = false;
            Changed?.Invoke();
        }
    }
}

/// <summary>自动化日志条目。</summary>
public class AutoLogEntry
{
    public string Time = "";
    public string Message = "";
    public string Color = "#94a3b8";
}
