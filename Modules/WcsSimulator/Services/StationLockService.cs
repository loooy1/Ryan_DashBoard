using System.Text.Json;
using GRCS.Dashboard.Modules.WcsSimulator.Models;
using Microsoft.JSInterop;

namespace GRCS.Dashboard.Modules.WcsSimulator.Services;

/// <summary>
/// 站点锁定：自动化任务随机分配的接驳位/目标储位/分拣台，在流程完成前不可重复分配。
/// 锁存 localStorage（grcs_station_locks），以流程终点任务（段2 或分拣段1）的 FINISHED 事件为解锁信号，
/// 惰性释放：下次分配前把已完成流程的锁清掉，未完成流程占用的站点继续锁定。
/// </summary>
public class StationLockService
{
    private const string StoreKey = "grcs_station_locks";
    private readonly IJSRuntime _js;
    private readonly LocalStoreService _store;
    private readonly TaskStageHub _stageHub;
    private static readonly JsonSerializerOptions Opts = new() { PropertyNameCaseInsensitive = true };

    public StationLockService(IJSRuntime js, LocalStoreService store, TaskStageHub stageHub)
    { _js = js; _store = store; _stageHub = stageHub; }

    /// <summary>返回当前仍被锁定（流程未完成）的站点；顺带把已完成流程的锁惰性释放。</summary>
    public async Task<HashSet<string>> GetLockedAsync()
    {
        var map = Load();
        if (map.Count == 0) return [];
        var finished = await GetFinishedTaskIdsAsync();
        if (finished.Count > 0)
        {
            var dirty = false;
            foreach (var (st, entry) in map.Where(kv => finished.Contains(kv.Value.TaskId)).ToList())
            {
                map.Remove(st);
                dirty = true;
            }
            if (dirty) await SaveAsync(map);
        }
        return map.Keys.ToHashSet();
    }

    /// <summary>为流程终点任务锁定站点（流程 FINISHED 后自动解锁；已锁定站点忽略）。</summary>
    public async Task AcquireAsync(string station, string flowEndTaskId)
    {
        if (string.IsNullOrEmpty(station)) return;
        var map = Load();
        if (map.ContainsKey(station)) return;
        map[station] = new StationLockEntry { TaskId = flowEndTaskId, Time = DateTime.Now.ToString("O") };
        await SaveAsync(map);
    }

    /// <summary>
    /// 已完成任务号集合：读 TaskStageHub 共享缓存（优化前每次调用全量拉一次 task-stages）。
    /// 缓存覆盖最近 1000 条事件（旧实现只拉 200 条），锁释放判定反而更准。
    /// </summary>
    private async Task<HashSet<string>> GetFinishedTaskIdsAsync()
    {
        try
        {
            await _stageHub.EnsureStartedAsync();
            return _stageHub.FinishedTaskIds;
        }
        catch { return []; }
    }

    private Dictionary<string, StationLockEntry> Load()
    {
        try
        {
            var s = _store[StoreKey];
            if (!string.IsNullOrEmpty(s) && s != "null")
                return JsonSerializer.Deserialize<Dictionary<string, StationLockEntry>>(s, Opts) ?? [];
        }
        catch { }
        return [];
    }

    private async Task SaveAsync(Dictionary<string, StationLockEntry> map)
    {
        await _store.SetAsync(_js, StoreKey, JsonSerializer.Serialize(map));
    }
}

/// <summary>站点锁条目：锁定该站点的流程终点任务 ID 与锁定时间。</summary>
public class StationLockEntry
{
    public string TaskId { get; set; } = "";
    public string? Time { get; set; }
}
