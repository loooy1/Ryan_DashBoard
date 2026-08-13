using System.Text.Json;
using Microsoft.JSInterop;

namespace GRCS.Dashboard.Modules.WcsSimulator.Services;

/// <summary>
/// 入库货物码管理：货物码在段1（空托入库）完成、生成到达卡片时才确定（WCS 给货物信息），
/// 自动化任务下发段2 与信号交互页生成到达卡片共用同一货物码。
/// 映射按段1 TaskId 存 localStorage（grcs_cargo_codes），先读后写保证幂等。
/// </summary>
public class CargoCodeService
{
    private const string StoreKey = "grcs_cargo_codes";
    private readonly IJSRuntime _js;
    private readonly LocalStoreService _store;
    private static readonly JsonSerializerOptions Opts = new() { PropertyNameCaseInsensitive = true };

    public CargoCodeService(IJSRuntime js, LocalStoreService store)
    { _js = js; _store = store; }

    /// <summary>取段1 任务对应的货物码；没有则自动生成一个并持久化（先读后写，多处调用得到同一码）。</summary>
    public async Task<string> EnsureAsync(string seg1TaskId)
    {
        var map = Load();
        if (map.TryGetValue(seg1TaskId, out var existing)) return existing;
        var code = "SimCargo_" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString("x").ToUpper();
        map[seg1TaskId] = code;
        await _store.SetAsync(_js, StoreKey, JsonSerializer.Serialize(map));
        return code;
    }

    private Dictionary<string, string> Load()
    {
        try
        {
            var s = _store[StoreKey];
            if (!string.IsNullOrEmpty(s) && s != "null")
                return JsonSerializer.Deserialize<Dictionary<string, string>>(s, Opts) ?? [];
        }
        catch { }
        return [];
    }
}
