using GRCS.Dashboard.Modules.WcsSimulator.Models;

namespace GRCS.Dashboard.Modules.WcsSimulator.Services;

/// <summary>
/// 任务台账遥控壳（Skill E：数据源已下沉到 GrcsBackend LedgerStore + SQLite）。
/// GetAsync 读后端（2s 缓存收敛）；AppendAsync 转 POST（后端写入，手动任务/自动任务同一份）；
/// ClearAsync 转 DELETE。换浏览器/清缓存数据仍在。
/// </summary>
public class TaskLedgerService
{
    private const int Limit = 2000;
    private readonly WcsApiClient _api;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private List<TaskLedgerEntry>? _cache;
    private DateTime _lastFetch = DateTime.MinValue;

    public TaskLedgerService(WcsApiClient api) => _api = api;

    public async Task<List<TaskLedgerEntry>> GetAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (_cache != null && DateTime.UtcNow - _lastFetch < TimeSpan.FromSeconds(2)) return _cache;
            var list = await _api.GetAsync<List<TaskLedgerEntry>>($"/api/wcs/ledger?limit={Limit}") ?? [];
            _cache = list;
            _lastFetch = DateTime.UtcNow;
            return _cache;
        }
        finally { _gate.Release(); }
    }

    /// <summary>追加条目（后端 SQLite 持久化，前端缓存失效下轮重读）。</summary>
    public async Task AppendAsync(List<TaskLedgerEntry> entries)
    {
        if (entries.Count == 0) return;
        await _gate.WaitAsync();
        try
        {
            await _api.PostAsync("/api/wcs/ledger", entries);
            _cache = null;
        }
        finally { _gate.Release(); }
    }

    public async Task ClearAsync()
    {
        await _gate.WaitAsync();
        try
        {
            await _api.DeleteAsync("/api/wcs/ledger");
            _cache = [];
        }
        finally { _gate.Release(); }
    }
}
