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
    private const string StoreKey = "grcs_cargo_codes";   // localStorage 键：段1 TaskId → 货物码 的映射
    private readonly IJSRuntime _js;                      // JS 互操作（写 localStorage 用）
    private readonly LocalStoreService _store;            // 统一本地存储封装（与各页面读写同一份数据）
    private static readonly JsonSerializerOptions Opts = new() { PropertyNameCaseInsensitive = true }; // 大小写不敏感，兼容旧版/手改数据

    public CargoCodeService(IJSRuntime js, LocalStoreService store)
    { _js = js; _store = store; } // 由 DI 注入

    /// <summary>
    /// 取段1 任务对应的货物码；没有则自动生成一个并持久化（先读后写，多处调用得到同一码）。
    /// 生成规则 SimCargo_ + UTC 毫秒时间戳（十六进制），保证唯一。
    /// 两个入口共用：自动任务服务下发入库段2 前、信号交互页生成到达卡片时（自动模式），
    /// 二者必须拿到同一个货物码，否则段2 与 container_ready 的容器对不上。
    /// </summary>
    public async Task<string> EnsureAsync(string seg1TaskId)
    {
        var map = Load();
        if (map.TryGetValue(seg1TaskId, out var existing)) return existing;
        var code = "SimCargo_" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString("x").ToUpper();
        map[seg1TaskId] = code;
        await _store.SetAsync(_js, StoreKey, JsonSerializer.Serialize(map));
        return code;
    }

    /// <summary>容错读取映射：数据缺失/损坏/格式不符一律返回空字典，不让 localStorage 问题阻断任务流程。</summary>
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
