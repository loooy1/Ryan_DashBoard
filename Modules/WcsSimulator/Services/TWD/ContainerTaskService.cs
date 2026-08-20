using System.Text.Json;

using GRCS.Dashboard.Modules.WcsSimulator.Services;

namespace GRCS.Dashboard.Modules.WcsSimulator.Services.TWD;

/// <summary>
/// 批量容器任务遥控壳（Skill E：执行逻辑已下沉到 GrcsBackend ContainerTaskRunner）。
/// ExecuteAsync / RefreshInventoryAsync 转 POST；状态/库存/日志来自 AutomationHub 快照。
/// </summary>
public class ContainerTaskService : IDisposable
{
    private readonly WcsApiClient _api;
    private readonly AutomationHub _hub;

    /// <summary>本标签页唯一 id（跨标签页区分发起者；随执行请求上传）。</summary>
    public string TabId { get; set; } = Guid.NewGuid().ToString("N");

    public ContainerTaskService(WcsApiClient api, AutomationHub hub)
    {
        _api = api;
        _hub = hub;
        _hub.Changed += OnHubChanged;
    }

    public bool Busy => _hub.Status.ContainerBusy;
    public int Done => _hub.Status.ContainerDone;
    public int Total => _hub.Status.ContainerTotal;
    public string Status => _hub.Status.ContainerStatus;

    public int EmptyPallets => _hub.Status.ContainerInventory.EmptyPallets;
    public int LoadedPallets => _hub.Status.ContainerInventory.LoadedPallets;
    public int Cargos => _hub.Status.ContainerInventory.Cargos;
    public int PairedCargos => _hub.Status.ContainerInventory.PairedCargos;

    /// <summary>日志（后端单一日志流，与轮询自动化共用）。</summary>
    public List<AutoLogEntry> Logs => _hub.Logs;

    public event Action? Changed;
    private void OnHubChanged() => Changed?.Invoke();

    public async Task<string> RefreshInventoryAsync()
    {
        var json = await _api.PostAsync("/api/wcs/auto/container/refresh", new { });
        return ParseMessage(json);
    }

    public async Task<string> ExecuteAsync(int flow, int count, int interval)
    {
        var json = await _api.PostAsync("/api/wcs/auto/container/execute", new { flow, count, interval, tabId = TabId });
        return ParseMessage(json);
    }

    public void ClearLogs() => _hub.ClearLogs();

    private static string ParseMessage(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("message", out var m))
                return m.GetString() ?? json;
        }
        catch { }
        return json;
    }

    public void Dispose() => _hub.Changed -= OnHubChanged;
}
