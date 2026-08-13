using System.Text.Json;
using Microsoft.JSInterop;

namespace GRCS.Dashboard.Modules.WcsSimulator.Services;

/// <summary>
/// localStorage 内存缓存层（scoped = per-browser-tab singleton）。
/// App 启动时一次性加载所有 key 到内存，之后所有读操作同步返回（0ms，无 JS 边界），
/// 写操作双写内存 + localStorage。
/// </summary>
public class LocalStoreService
{
    private readonly Dictionary<string, string?> _cache = new();

    public bool Loaded { get; private set; }

    /// <summary>一次性加载所有 key 到内存（启动时调用一次，约 10ms）。</summary>
    public async Task PreloadAsync(IJSRuntime js)
    {
        if (Loaded) return;
        try
        {
            var keysJson = JsonSerializer.Serialize(AllKeys);
            var batch = await js.InvokeAsync<string>("grcsStoreLoadAll", keysJson);
            if (!string.IsNullOrEmpty(batch) && batch != "null")
            {
                var store = JsonSerializer.Deserialize<Dictionary<string, string?>>(batch);
                if (store != null)
                {
                    foreach (var kv in store)
                        _cache[kv.Key] = kv.Value;
                }
            }
        }
        catch { /* localStorage 不可用时忽略，后续 Get() 返回 null */ }
        Loaded = true;
    }

    /// <summary>同步读取（0ms，无 JS 边界）。</summary>
    public string? Get(string key) =>
        _cache.TryGetValue(key, out var v) ? v : null;

    /// <summary>同步索引器。</summary>
    public string? this[string key] => Get(key);

    /// <summary>双写：内存立即更新 + localStorage 异步持久化。</summary>
    public async Task SetAsync(IJSRuntime js, string key, string value)
    {
        _cache[key] = value;
        try { await js.InvokeVoidAsync("grcsStoreSave", key, value); } catch { }
    }

    /// <summary>批量双写（一次 JS 调用持久化多个 key，减少边界开销）。</summary>
    public async Task SetManyAsync(IJSRuntime js, params (string Key, string Value)[] items)
    {
        foreach (var (key, value) in items)
            _cache[key] = value;
        try
        {
            foreach (var (key, value) in items)
                await js.InvokeVoidAsync("grcsStoreSave", key, value);
        }
        catch { }
    }

    /// <summary>预加载的 key 列表（排除巨型 history/ledger key）。</summary>
    private static readonly string[] AllKeys =
    [
        // 共享配置
        "grcs_warehouse", "grcs_wcs_url", "grcs_grcs_url",
        // 自动模式开关
        "grcs_si_auto_mode", "grcs_arrival_auto", "grcs_removal_auto", "grcs_ss_auto",
        // 折叠状态
        "grcs_si_collapsed", "grcs_ts_collapsed", "grcs_td_collapsed", "grcs_mr_collapsed",
        "grcs_inv_collapsed", "grcs_auto_collapsed", "grcs_adm_collapsed",
        "grcs_vehicle_collapsed", "grcs_th_collapsed",
        // 确认/跟踪集合
        "grcs_arrival_confirmed", "grcs_si_del_arrival", "grcs_removal_confirmed",
        "grcs_si_del_removal", "grcs_ss_sent", "grcs_ss_cards",
        // 表单/任务状态
        "grcs_td_state", "grcs_ts_deleted", "grcs_ts_events",
        // 服务数据
        "grcs_cargo_codes", "grcs_station_locks",
        "grcs_cargo_inventory", "grcs_cargo_inventory_at",
        // 地图缓存
        "grcs_map_stations",
    ];
}
