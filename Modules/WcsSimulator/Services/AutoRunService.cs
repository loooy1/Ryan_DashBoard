namespace GRCS.Dashboard.Modules.WcsSimulator.Services;

/// <summary>
/// 自动化轮询遥控壳（Skill E：执行逻辑已下沉到 GrcsBackend AutoRunHostedService）。
/// 本类只做遥控与展示：状态/日志来自 AutomationHub 轮询快照，启停/参数变更 POST 到后端。
/// 对外接口与旧版一致（页面代码无感知），关闭全部标签页自动化继续跑。
/// </summary>
public class AutoRunService : IDisposable
{
    private readonly WcsApiClient _api;
    private readonly AutomationHub _hub;

    /// <summary>本标签页唯一 id（跨标签页区分发起者；随启停请求上传）。</summary>
    public string TabId { get; set; } = Guid.NewGuid().ToString("N");

    public AutoRunService(WcsApiClient api, AutomationHub hub)
    {
        _api = api;
        _hub = hub;
        _hub.Changed += OnHubChanged;
    }

    public bool Running => _hub.Status.Running;
    public int Dispatched => _hub.Status.Dispatched;

    public int Interval
    {
        get => _hub.Status.Interval;
        set { if (value != _hub.Status.Interval) _ = _api.PostAsync("/api/wcs/auto/interval", new { interval = value }); }
    }

    public int FlowMode
    {
        get => _hub.Status.FlowMode;
        set { if (value != _hub.Status.FlowMode) _ = _api.PostAsync("/api/wcs/auto/flowmode", new { flowMode = value }); }
    }

    /// <summary>日志（后端单一日志流，与批量任务共用）。</summary>
    public List<AutoLogEntry> Logs => _hub.Logs;

    public event Action? Changed;
    private void OnHubChanged() => Changed?.Invoke();

    /// <summary>启停切换：POST 到后端（互斥由后端 AutomationGate 硬保证），成功后乐观更新快照。</summary>
    public async Task ToggleAsync()
    {
        var isStart = !Running;
        var json = isStart
            ? await _api.PostAsync("/api/wcs/auto/start", new { tabId = TabId })
            : await _api.PostAsync("/api/wcs/auto/stop", new { tabId = TabId });
        if (isStart)
        {
            // 互斥被拒时后端会在日志里给出原因；仅当 success=true 才乐观翻转
            if (json.Contains("\"success\":true", StringComparison.OrdinalIgnoreCase))
                _hub.ApplyRunning(true);
        }
        else
        {
            _hub.ApplyRunning(false);
        }
    }

    public void ClearLogs() => _hub.ClearLogs();

    public void Dispose() => _hub.Changed -= OnHubChanged;
}
