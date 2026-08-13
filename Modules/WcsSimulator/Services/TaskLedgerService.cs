using System.Text.Json;
using GRCS.Dashboard.Modules.WcsSimulator.Models;
using Microsoft.JSInterop;

namespace GRCS.Dashboard.Modules.WcsSimulator.Services;

/// <summary>
/// 任务台账内存缓存（scoped = per-browser-tab singleton）。
/// 台账 grcs_task_ledger 是唯一数据源但体积大（上限 2000 条），不适合放入 LocalStoreService 预加载。
/// 本服务首读时从 localStorage 拉一次并缓存，之后所有读都是内存操作（0 次 JS 边界 + 0 次 JSON 反序列化）；
/// 本标签页所有写入走 AppendAsync 同步更新缓存并写穿 localStorage（JS 端负责旧 history 迁移与 2000 上限合并）。
/// 信号轮询、页面刷新不再每次全量加载台账。
/// </summary>
public class TaskLedgerService
{
    private const int Limit = 2000;
    private readonly IJSRuntime _js;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private static readonly JsonSerializerOptions Opts = new() { PropertyNameCaseInsensitive = true };
    private List<TaskLedgerEntry>? _cache;
    private int _lastVersion = -1;
    private bool _versionSupported = true;

    public TaskLedgerService(IJSRuntime js) => _js = js;

    /// <summary>读台账（内存缓存；首次从 localStorage 拉取，旧 history 由 JS 端顺带迁移）。
    /// 其他标签页写入台账时 JS 端版本号 +1，此处发现版本变化即重读，保证跨标签页可见。</summary>
    public async Task<List<TaskLedgerEntry>> GetAsync()
    {
        int version = -1;
        if (_versionSupported)
        {
            try { version = await _js.InvokeAsync<int>("grcsLedgerVersion"); }
            catch { _versionSupported = false; } // 旧 index.html 无此函数：退化为每次直读 localStorage
        }
        if (_versionSupported && _cache != null && version == _lastVersion) return _cache;
        var list = new List<TaskLedgerEntry>();
        try
        {
            var json = await _js.InvokeAsync<string>("grcsLoadTaskLedgerMigrated");
            if (!string.IsNullOrEmpty(json) && json != "null")
                list = JsonSerializer.Deserialize<List<TaskLedgerEntry>>(json, Opts) ?? [];
        }
        catch { }
        _cache = list;
        _lastVersion = version;
        return _cache;
    }

    /// <summary>追加条目（新条目在前，与 JS 端合并顺序一致），写穿 localStorage。</summary>
    public async Task AppendAsync(List<TaskLedgerEntry> entries)
    {
        if (entries.Count == 0) return;
        await _gate.WaitAsync();
        try
        {
            var cur = await GetAsync();
            _cache = entries.Concat(cur).Take(Limit).ToList();
            try { await _js.InvokeVoidAsync("grcsSaveHistory", JsonSerializer.Serialize(entries)); }
            catch { _cache = null; } // 写失败：缓存失效，下读重新同步 localStorage
        }
        finally { _gate.Release(); }
    }

    /// <summary>清空台账（内存 + localStorage）。</summary>
    public async Task ClearAsync()
    {
        await _gate.WaitAsync();
        try
        {
            _cache = [];
            try { await _js.InvokeVoidAsync("grcsClearHistory"); } catch { }
        }
        finally { _gate.Release(); }
    }
}
