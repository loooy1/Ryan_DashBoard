using System.Text.Json;
using GRCS.Dashboard.Modules.WcsSimulator.Extensions;
using GRCS.Dashboard.Modules.WcsSimulator.Models;
using Microsoft.JSInterop;

namespace GRCS.Dashboard.Modules.WcsSimulator.Services;

/// <summary>
/// 自动容器任务下发服务（跨页面导航存活）。
/// 执行批量两段式容器任务（入库/出库/分拣），状态通过 Changed 事件通知页面刷新。
/// 切模块后执行不中断，返回页面时状态自动恢复。
/// </summary>
public class ContainerTaskService
{
    private const string WcsUrlKey = "grcs_wcs_url";
    private const string DefaultWcsUrl = "http://localhost:8230";
    private readonly IWcsService _wcs;
    private readonly CargoCodeService _cargoCodes;
    private readonly StationLockService _stationLocks;
    private readonly TaskStageHub _stageHub;
    private static readonly JsonSerializerOptions Opts = new() { PropertyNameCaseInsensitive = true };

    // ── 执行状态（跨导航存活）──
    public bool Busy { get; private set; }
    public int Done { get; private set; }
    public int Total { get; private set; }
    public string Status { get; private set; } = "";

    /// <summary>详细执行日志（跨导航存活）。</summary>
    public List<AutoLogEntry> Logs { get; } = [];

    // ── 库存缓存（查询后缓存，执行时直接用）──
    public int EmptyPallets { get; private set; }
    public int LoadedPallets { get; private set; }
    public int Cargos { get; private set; }
    public int PairedCargos { get; private set; }

    /// <summary>状态变化通知，页面订阅后刷新 UI。</summary>
    public event Action? Changed;

    private List<(string Code, string Station)> _cachedEmptyPallets = [];
    private List<(string Code, string Station)> _cachedLoadedPallets = [];
    private List<(string Code, string Station)> _cachedCargos = [];
    private List<(string Code, string Station)> _cachedPairedCargos = [];

    private string _baseUrl = "http://localhost:8224";
    private string _wcsBaseUrl = DefaultWcsUrl;
    private string _sceneName = "";
    private List<MapStationLite> _mapStations = [];
    private readonly LocalStoreService _store;
    private readonly TaskLedgerService _ledger;

    public ContainerTaskService(IWcsService wcs, CargoCodeService cargoCodes, StationLockService stationLocks, TaskStageHub stageHub, LocalStoreService store, TaskLedgerService ledger)
    {
        _wcs = wcs;
        _cargoCodes = cargoCodes;
        _stationLocks = stationLocks;
        _stageHub = stageHub;
        _store = store;
        _ledger = ledger;
    }

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
        }
        catch { }
    }

    public void ClearLogs() { Logs.Clear(); Changed?.Invoke(); }

    /// <summary>
    /// 日志通知合并（防抖）：与 AutoRunService.Log 同款机制。
    /// 批量执行（ExecuteAsync 的段1 循环 + 段2 并行等待）时日志密集连发，
    /// 150ms 窗口内的多条日志合并成一次 Changed，页面每轮最多重渲染一次。
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

    /// <summary>保存任务台账（唯一数据源，取代下发历史，内存缓存写穿）。ContainerCode=托盘号，CargoCode=货物号。</summary>
    private async Task SaveLedgerAsync(List<TaskLedgerEntry> entries) => await _ledger.AppendAsync(entries);

    /// <summary>查询库存并缓存结果（页面可独立调用，执行时也会自动调用）。</summary>
    public async Task<string> RefreshInventoryAsync()
    {
        Log("📋 查询库存中...", "#94a3b8");
        Status = "查询中...";
        Changed?.Invoke();
        var allPallets = new List<(string Code, string Station)>();
        var allCargos = new List<(string Code, string Station)>();
        var resultMsg = "";

        LoadConfig();
        try
        {
            var (ok, _, json) = await _wcs.QueryCargoInventoryAsync(_baseUrl, scene: _sceneName);
            if (ok)
            {
                var result = JsonSerializer.Deserialize<CargoQueryResult>(json, Opts);
                if (result?.Data?.Records != null)
                {
                    int locked = 0, loaded = 0, nocode = 0;
                    var skippedCodes = new List<string>();
                    foreach (var c in result.Data.Records)
                    {
                        if (c.IsLocked) { locked++; continue; }
                        if (c.IsLoaded) { loaded++; continue; }
                        var loc = c.CurrentStationCode;
                        if (string.IsNullOrEmpty(c.Code) || string.IsNullOrEmpty(loc)) { nocode++; skippedCodes.Add((c.Code ?? "null") + "@" + (loc ?? "null")); continue; }
                        if (c.IsPallet())
                            allPallets.Add((c.Code, loc));
                        else if (c.IsCargo())
                            allCargos.Add((c.Code, loc));
                    }
                    resultMsg = "总数 " + result.Data.Records.Count + " ";
                    if (locked > 0 || loaded > 0 || nocode > 0)
                        resultMsg += "跳过:" + locked + "锁/" + loaded + "途/" + nocode + "码 ";
                    if (skippedCodes.Count > 0)
                        resultMsg += "[" + string.Join(",", skippedCodes) + "] ";
                    resultMsg += "→ ";
                }
            }
        }
        catch (Exception ex) { Log("❌ 库存查询异常：" + ex.Message, "#f87171"); }

        // 区分空托 / 带货托 / 货物
        var cargoMarks = new HashSet<string>(allCargos.Select(c => c.Station));
        var palletMarks = new HashSet<string>(allPallets.Select(p => p.Station));
        _cachedEmptyPallets = allPallets.Where(p => !cargoMarks.Contains(p.Station)).ToList();
        _cachedLoadedPallets = allPallets.Where(p => cargoMarks.Contains(p.Station)).ToList();
        _cachedCargos = allCargos.Where(c => !palletMarks.Contains(c.Station)).ToList();
        _cachedPairedCargos = allCargos.Where(c => palletMarks.Contains(c.Station)).ToList();

        EmptyPallets = _cachedEmptyPallets.Count;
        LoadedPallets = _cachedLoadedPallets.Count;
        Cargos = _cachedCargos.Count;
        PairedCargos = _cachedPairedCargos.Count;

        Status = resultMsg + "空托 " + EmptyPallets + " / 带货托 " + LoadedPallets + " / 货物 " + Cargos + " / 配对货 " + PairedCargos;
        Log("库存查询完成：空托 " + EmptyPallets + " / 带货托 " + LoadedPallets + " / 货物 " + Cargos + " / 配对货 " + PairedCargos, "#4ade80");
        Changed?.Invoke();
        return Status;
    }

    /// <summary>
    /// 执行批量两段式容器任务（入库/出库/分拣）。
    /// 段1 下发 → 无限等待 FINISHED → 段2 下发。切模块后继续执行，返回页面时状态自动恢复。
    /// </summary>
    public async Task<string> ExecuteAsync(int flow, int count, int interval)
    {
        LoadConfig();
        if (_mapStations.Count == 0) { var m = "❌ 请先在「地图信息」页读取 map.json"; Log(m, "#f87171"); return m; }
        if (string.IsNullOrEmpty(_sceneName)) { var m = "❌ 请填写场景名称"; Log(m, "#f87171"); return m; }

        // 库存未初始化时自动查询
        if (_cachedEmptyPallets.Count == 0 && _cachedLoadedPallets.Count == 0 && _cachedCargos.Count == 0)
            await RefreshInventoryAsync();

        var flowName = flow switch { 1 => "空托盘入库", 2 => "带货托盘出库", _ => "带货托盘分拣" };
        Log("🚀 开始执行：" + flowName + " × " + count + "（间隔 " + interval + " s），空托 " + EmptyPallets + " / 带货托 " + LoadedPallets + " / 配对货 " + PairedCargos, "#60a5fa");

        Busy = true; Done = 0; Total = count;
        Status = "";
        Changed?.Invoke();

        var rand = Random.Shared;
        int okCount = 0; var errors = new List<string>();
        var ctaTs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString("x").ToUpper();

        var storages = _mapStations.Where(s => s.StaEnable && (s.StationType & MapStationTypeBits.StorageLocation) != 0).ToList();
        var lockedStations = await _stationLocks.GetLockedAsync();
        var transferPoints = _mapStations.Where(s => s.StaEnable && (s.StationType & MapStationTypeBits.TransferPoint) != 0).ToList();
        var pickingStations = _mapStations.Where(s => s.StaEnable && (s.StationType & MapStationTypeBits.PeopleStation) != 0).ToList();
        var occupiedMarks = new HashSet<string>(_cachedEmptyPallets.Select(p => p.Station).Concat(_cachedLoadedPallets.Select(p => p.Station)).Concat(_cachedCargos.Select(c => c.Station)).Concat(_cachedPairedCargos.Select(c => c.Station)));
        var emptyStorages = storages.Where(s => !occupiedMarks.Contains(s.Mark) && !lockedStations.Contains(s.Mark)).ToList();

        // 消费列表（副本，防止重复选同一容器）
        var emptyPallets = _cachedEmptyPallets.ToList();
        var loadedPallets = _cachedLoadedPallets.ToList();
        var pairedCargos = _cachedPairedCargos.ToList();

        // ── 阶段1：下发所有段1，记录段2 待发信息 ──
        var seg2s = new List<(int No, string Id1, string Id2, string Type2, string? CC2, List<string> Sta2, string Seg2Pallet, string Seg2Cargo)>();
        string seg1PalletCode = "";   // 分拣段1 补查的托盘号（台账 ContainerCode 用）
        for (int i = 0; i < count; i++)
        {
            try
            {
                string taskType1, taskType2 = null!;
                List<string> stations1, stations2 = null!;
                string cc1; string? cc2 = null;

                if (flow == 1) // 空托盘入库
                {
                    if (emptyPallets.Count == 0) { var m2 = "#" + (i + 1) + " 无可用空托盘"; errors.Add(m2); Log("⚠ " + m2, "#fbbf24"); continue; }
                    if (transferPoints.Count == 0) { var m2 = "#" + (i + 1) + " 无可用接驳位"; errors.Add(m2); Log("⚠ " + m2, "#fbbf24"); continue; }
                    if (emptyStorages.Count == 0) { var m2 = "#" + (i + 1) + " 无空货位"; errors.Add(m2); Log("⚠ " + m2, "#fbbf24"); continue; }
                    var pallet = emptyPallets[rand.Next(emptyPallets.Count)];
                    var tpSt = transferPoints[rand.Next(transferPoints.Count)];
                    var dstSt = emptyStorages[rand.Next(emptyStorages.Count)];
                    emptyStorages.Remove(dstSt);
                    var srcSt = storages.FirstOrDefault(s => s.Mark == pallet.Station);
                    taskType1 = "CONTAINER_CARRY_INBOUND";
                    stations1 = [srcSt != null ? srcSt.ToWcsCode() : pallet.Station, tpSt.ToWcsCode()];
                    taskType2 = "CARGO_CARRY_INBOUND";
                    stations2 = [tpSt.ToWcsCode(), dstSt.ToWcsCode()];
                    cc1 = pallet.Code;
                    cc2 = null;
                    await _stationLocks.AcquireAsync(dstSt.Mark, "SimAuto_" + ctaTs + "_" + i + "b");
                    emptyPallets.Remove(pallet);
                }
                else if (flow == 2) // 带货托盘出库
                {
                    if (loadedPallets.Count == 0) { var m2 = "#" + (i + 1) + " 无可用带货托盘"; errors.Add(m2); Log("⚠ " + m2, "#fbbf24"); continue; }
                    if (transferPoints.Count == 0) { var m2 = "#" + (i + 1) + " 无可用接驳位"; errors.Add(m2); Log("⚠ " + m2, "#fbbf24"); continue; }
                    if (emptyStorages.Count == 0) { var m2 = "#" + (i + 1) + " 无空货位"; errors.Add(m2); Log("⚠ " + m2, "#fbbf24"); continue; }
                    var loaded = loadedPallets[rand.Next(loadedPallets.Count)];
                    var cargo = pairedCargos.FirstOrDefault(c => c.Station == loaded.Station);
                    if (cargo.Code == null) { var m2 = "#" + (i + 1) + " 带货托盘 " + loaded.Code + " 无对应货物"; errors.Add(m2); Log("⚠ " + m2, "#fbbf24"); continue; }
                    var tpSt = transferPoints[rand.Next(transferPoints.Count)];
                    var dstSt = emptyStorages[rand.Next(emptyStorages.Count)];
                    emptyStorages.Remove(dstSt);
                    var srcSt = storages.FirstOrDefault(s => s.Mark == loaded.Station);
                    taskType1 = "CARGO_CARRY_OUTBOUND";
                    stations1 = [srcSt != null ? srcSt.ToWcsCode() : loaded.Station, tpSt.ToWcsCode()];
                    taskType2 = "CONTAINER_CARRY_OUTBOUND";
                    stations2 = [tpSt.ToWcsCode(), dstSt.ToWcsCode()];
                    cc1 = cargo.Code;
                    cc2 = loaded.Code;
                    await _stationLocks.AcquireAsync(dstSt.Mark, "SimAuto_" + ctaTs + "_" + i + "b");
                    loadedPallets.Remove(loaded);
                    pairedCargos.Remove(cargo);
                }
                else // 分拣
                {
                    if (pairedCargos.Count == 0) { var m2 = "#" + (i + 1) + " 无可用配对货"; errors.Add(m2); Log("⚠ " + m2, "#fbbf24"); continue; }
                    if (pickingStations.Count == 0) { var m2 = "#" + (i + 1) + " 无人工分拣台"; errors.Add(m2); Log("⚠ " + m2, "#fbbf24"); continue; }
                    var cargo = pairedCargos[rand.Next(pairedCargos.Count)];
                    var pickSt = pickingStations[rand.Next(pickingStations.Count)];
                    var srcSt = storages.FirstOrDefault(s => s.Mark == cargo.Station);
                    taskType1 = "SORTING";
                    stations1 = [srcSt != null ? srcSt.ToWcsCode() : cargo.Station, pickSt.ToWcsCode()];
                    cc1 = cargo.Code;
                    seg1PalletCode = _cachedLoadedPallets.FirstOrDefault(p => p.Station == cargo.Station).Code ?? "";
                    pairedCargos.Remove(cargo);
                }

                // 下发段1
                var id1 = "SimAuto_" + ctaTs + "_" + i + "a";
                Log("#" + (i + 1) + " 段1 " + id1 + " " + taskType1 + " " + cc1 + " → " + string.Join("→", stations1), "#60a5fa");
                var payload1 = new WcsTaskGroup
                {
                    GroupId = "G_" + id1, MsgTime = DateTime.Now.ToString("O"), PriorityCode = 50,
                    Warehouse = _sceneName,
                    Tasks = [new WcsTaskItem { TaskId = id1, TaskType = taskType1, ContainerCode = cc1, StationCode = stations1 }]
                };
                var (ok1, code1, j1) = await _wcs.SendTaskGroupAsync(_baseUrl, "1.0", payload1);
                var seg1Pallet = taskType1 == "CONTAINER_CARRY_INBOUND" ? cc1 : (taskType1 == "CARGO_CARRY_OUTBOUND" ? cc2 ?? "" : seg1PalletCode);
                var seg1Cargo = taskType1 == "CONTAINER_CARRY_INBOUND" ? "" : cc1;
                await SaveLedgerAsync([new TaskLedgerEntry { TaskId = id1, TaskType = taskType1, ContainerCode = seg1Pallet, CargoCode = seg1Cargo, StationCode = stations1, Warehouse = _sceneName, Time = DateTime.Now.ToString("O"), Ok = ok1, StatusCode = code1 }]);
                if (!ok1) { var m2 = "#" + (i + 1) + " 段1 下发失败 HTTP" + code1; errors.Add(m2); Log("❌ " + m2, "#f87171"); continue; }

                var id2 = "SimAuto_" + ctaTs + "_" + i + "b";
                string seg2Pallet, seg2Cargo;
                if (flow == 1) { seg2Pallet = cc1; seg2Cargo = ""; }                 // 入库段2：托盘=段1托盘，货物动态生成
                else if (flow == 2) { seg2Pallet = cc2 ?? ""; seg2Cargo = cc1; }     // 出库段2：托盘=段2托盘，货物=段1货物
                else { seg2Pallet = seg1PalletCode; seg2Cargo = cc1; }               // 分拣无段2（占位，不会被使用）
                seg2s.Add((i + 1, id1, id2, taskType2, cc2, stations2, seg2Pallet, seg2Cargo));
                Done++;
                Status = "段1 已下发 " + Done + "/" + Total;
                Changed?.Invoke();

                if (interval > 0 && i < count - 1)
                    await Task.Delay(interval * 1000);
            }
            catch (Exception ex) { var m2 = "#" + (i + 1) + " 异常: " + ex.Message; errors.Add(m2); Log("❌ " + m2, "#f87171"); }
        }

        // ── 阶段2：并行等待所有段1 FINISHED，谁先完成谁立刻下发段2 ──
        var lockObj = new object();
        await Task.WhenAll(seg2s.Select(async seg2 =>
        {
            var (no, id1, id2, type2, cc2, sta2, seg2Pallet, seg2Cargo) = seg2;
            try
            {
                if (type2 == null) { lock (lockObj) { okCount++; } Log("#" + no + " 分拣流程无段2，跳过", "#94a3b8"); return; }

                Log("#" + no + " 等待段1 完成 " + id1 + " ...", "#fbbf24");
                Changed?.Invoke();
                await WaitFinishedAsync(id1);
                Log("#" + no + " 段1 " + id1 + " 完成", "#4ade80");

                var cc2Final = (type2 == "CARGO_CARRY_INBOUND" ? await _cargoCodes.EnsureAsync(id1) : cc2)!;
                Log("#" + no + " 段2 " + id2 + " " + type2 + " " + cc2Final + " → " + string.Join("→", sta2), "#60a5fa");
                Changed?.Invoke();

                var payload2 = new WcsTaskGroup
                {
                    GroupId = "G_" + id2, MsgTime = DateTime.Now.ToString("O"), PriorityCode = 50,
                    Warehouse = _sceneName,
                    Tasks = [new WcsTaskItem { TaskId = id2, TaskType = type2, ContainerCode = cc2Final, StationCode = sta2 }]
                };
                var (ok2, code2, j2) = await _wcs.SendTaskGroupAsync(_baseUrl, "1.0", payload2);
                var seg2CargoFinal = type2 == "CARGO_CARRY_INBOUND" ? cc2Final : seg2Cargo;
                await SaveLedgerAsync([new TaskLedgerEntry { TaskId = id2, TaskType = type2, ContainerCode = seg2Pallet, CargoCode = seg2CargoFinal, StationCode = sta2, Warehouse = _sceneName, Time = DateTime.Now.ToString("O"), Ok = ok2, StatusCode = code2 }]);
                lock (lockObj)
                {
                    if (!ok2) { var m2 = "#" + no + " 段2 " + type2 + " 下发失败 HTTP" + code2; errors.Add(m2); Log("❌ " + m2, "#f87171"); }
                    else { okCount++; Log("✓ #" + no + " 段2 已下发 " + id2, "#4ade80"); }
                }
            }
            catch (Exception ex) { var m2 = "#" + no + " 段2 异常: " + ex.Message; lock (lockObj) { errors.Add(m2); } Log("❌ " + m2, "#f87171"); }
        }));

        Busy = false;
        var finalMsg = "完成 " + okCount + "/" + Total + (errors.Count > 0 ? " / " + errors.Count + " 个失败" : "");
        Status = finalMsg;
        Log((errors.Count == 0 ? "✅ " : "⚠️ ") + finalMsg, errors.Count == 0 ? "#4ade80" : "#fbbf24");
        Changed?.Invoke();

        return (errors.Count == 0 ? "✅" : "⚠️") + " 自动容器任务 " + finalMsg
            + (errors.Count > 0 ? "\n\n" + string.Join("\n", errors.Take(10)) : "");
    }

    /// <summary>无限等待任务 FINISHED（无超时，直到段1 完成）。共享轮询器替代各自 1s 轮询。</summary>
    private async Task WaitFinishedAsync(string taskId)
    {
        await _stageHub.WaitFinishedAsync(taskId);
    }
}
