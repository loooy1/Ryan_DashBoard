using System.Net.Http.Json;
using System.Text.Json;
using GRCS.Dashboard.Modules.WcsSimulator.Models;

namespace GRCS.Dashboard.Modules.WcsSimulator.Services;

/// <summary>
/// WCS 服务的 HTTP 实现：直接 POST JSON 到 GRCS 后端。
/// 对接真实后端时改 Program.cs 里的 DI 注册即可。
/// </summary>
public class MockWcsService : IWcsService
{
    private readonly HttpClient _http;

    public MockWcsService(HttpClient http) => _http = http;

    // ── 任务下发 ──

    /// <summary>向 GRCS 的 /api/v{version}/task_receive 发送任务组。</summary>
    public async Task<(bool Ok, int StatusCode, string Json)> SendTaskGroupAsync(
        string baseUrl, string apiVersion, WcsTaskGroup payload)
    {
        var url = $"{baseUrl.TrimEnd('/')}/api/v{apiVersion}/task_receive";
        return await PostAsync(url, payload);
    }

    // ── 车辆任务 ──

    /// <summary>向 GRCS 的 /api/RawOrder/ChangeFloor 发送车辆任务（MOVE_ONLY / CHANGE_FLOOR / CHARGE）。</summary>
    public async Task<(bool Ok, int StatusCode, string Json)> SendVehicleOrderAsync(
        string baseUrl, VehicleOrderRequest payload)
    {
        var url = $"{baseUrl.TrimEnd('/')}/api/RawOrder/ChangeFloor";
        return await PostAsync(url, payload);
    }

    // ── 库存查询 ──

    /// <summary>
    /// 向 GRCS 的 /api/Cargo 查询容器库存（GET，支持 Code / HomeStationScene / IsLocked 过滤 + 分页）。
    /// GRCS 侧原生支持 pageNo/pageSize 并返回 totalCount，前端分页展示只发前 200 条/页；
    /// 需要全量数据的业务调用方保持默认 pageSize=2000（与优化前行为一致）。
    /// </summary>
    public async Task<(bool Ok, int StatusCode, string Json)> QueryCargoInventoryAsync(
        string baseUrl, string? code = null, string? scene = null, string? locked = null,
        int pageNo = 1, int pageSize = 2000)
    {
        var url = $"{baseUrl.TrimEnd('/')}/api/Cargo?pageNo={pageNo}&pageSize={pageSize}";
        if (!string.IsNullOrWhiteSpace(code)) url += $"&SearchContextParams[Code]={Uri.EscapeDataString(code)}";
        if (!string.IsNullOrWhiteSpace(scene)) url += $"&SearchContextParams[HomeStationScene]={Uri.EscapeDataString(scene)}";
        if (!string.IsNullOrWhiteSpace(locked)) url += $"&SearchContextParams[IsLocked]={locked}";
        try
        {
            var resp = await _http.GetAsync(url);
            var body = await resp.Content.ReadAsStringAsync();
            return (resp.IsSuccessStatusCode, (int)resp.StatusCode, body);
        }
        catch (HttpRequestException ex)
        {
            return (false, 0, JsonSerializer.Serialize(new { error = ex.Message }));
        }
    }

    /// <summary>向 GRCS 的 /api/Cargo/{id} 发送 DELETE 删除容器库存。</summary>
    public async Task<(bool Ok, int StatusCode, string Json)> DeleteCargoAsync(string baseUrl, int id)
    {
        var url = $"{baseUrl.TrimEnd('/')}/api/Cargo/{id}";
        try
        {
            var resp = await _http.DeleteAsync(url);
            var body = await resp.Content.ReadAsStringAsync();
            return (resp.IsSuccessStatusCode, (int)resp.StatusCode, body);
        }
        catch (HttpRequestException ex)
        {
            return (false, 0, JsonSerializer.Serialize(new { error = ex.Message }));
        }
    }

    /// <summary>向 GRCS 的 /AutoContainerEnter 发送 GET 自动生成容器入库。</summary>
    public async Task<(bool Ok, int StatusCode, string Json)> AutoContainerEnterAsync(string baseUrl, string sceneName,
        string prefix = "container", int num = -1, int floor = -1, int type = 1)
    {
        var url = $"{baseUrl.TrimEnd('/')}/AutoContainerEnter?sceneName={Uri.EscapeDataString(sceneName)}"
            + $"&prefix={Uri.EscapeDataString(prefix)}&num={num}&floor={floor}&type={type}";
        try
        {
            var resp = await _http.GetAsync(url);
            var body = await resp.Content.ReadAsStringAsync();
            return (resp.IsSuccessStatusCode, (int)resp.StatusCode, body);
        }
        catch (HttpRequestException ex)
        {
            return (false, 0, JsonSerializer.Serialize(new { error = ex.Message }));
        }
    }

    // ── 分拣信号（WCS → GRCS 出站信号）──

    /// <summary>向 GRCS 的 /api/v{version}/container_operation_finish 发送分拣完成通知。</summary>
    public async Task<(bool Ok, int StatusCode, string Json)> SendOperationFinishAsync(
        string baseUrl, string apiVersion, WcsOperationFinishRequest payload)
    {
        var url = $"{baseUrl.TrimEnd('/')}/api/v{apiVersion}/container_operation_finish";
        return await PostAsync(url, payload);
    }

    /// <summary>向 GRCS 的 /api/v{version}/container_ready 发送货物到达通知（入库容器到达输送线末端）。</summary>
    public async Task<(bool Ok, int StatusCode, string Json)> SendContainerReadyAsync(
        string baseUrl, string apiVersion, WcsContainerReadyRequest payload)
    {
        var url = $"{baseUrl.TrimEnd('/')}/api/v{apiVersion}/container_ready";
        return await PostAsync(url, payload);
    }

    /// <summary>向 GRCS 的 /api/v{version}/container_remove 发送货物移除通知（出库容器离开输送线末端）。</summary>
    public async Task<(bool Ok, int StatusCode, string Json)> SendContainerRemoveAsync(
        string baseUrl, string apiVersion, WcsContainerRemoveRequest payload)
    {
        var url = $"{baseUrl.TrimEnd('/')}/api/v{apiVersion}/container_remove";
        return await PostAsync(url, payload);
    }

    // ── 任务阶段（WCS 后端管理接口 /api/wcs）──

    /// <summary>
    /// 查询任务阶段变化事件列表（GET /api/wcs/task-stages）。
    /// sinceId &gt; 0 时只返回 Id 更大的增量事件（WCS 后端 GetEventsSince，Id 为后端内存自增、单调不回绕），
    /// 供 TaskStageHub 增量合并；不带 sinceId 时后端返回最近 200 条（全量首拉/周期对账用）。
    /// </summary>
    public async Task<(bool Ok, int StatusCode, string Json)> GetTaskStageEventsAsync(string baseUrl, long sinceId = 0)
    {
        var url = $"{baseUrl.TrimEnd('/')}/api/wcs/task-stages";
        if (sinceId > 0) url += $"?sinceId={sinceId}";
        try
        {
            var resp = await _http.GetAsync(url);
            var body = await resp.Content.ReadAsStringAsync();
            return (resp.IsSuccessStatusCode, (int)resp.StatusCode, body);
        }
        catch (HttpRequestException ex)
        {
            return (false, 0, JsonSerializer.Serialize(new { error = ex.Message }));
        }
    }

    /// <summary>判断指定任务是否已到达某个阶段。</summary>
    public async Task<bool> HasTaskReachedStageAsync(string baseUrl, string taskId, string stage)
    {
        var (ok, _, json) = await GetTaskStageEventsAsync(baseUrl);
        if (!ok) return false;
        try
        {
            var events = System.Text.Json.JsonSerializer.Deserialize<List<GRCS.Dashboard.Modules.WcsSimulator.Models.StageChangeEvent>>(json,
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return events?.Any(e => e.TaskId == taskId && e.Stage == stage) ?? false;
        }
        catch { return false; }
    }

    /// <summary>删除指定任务的所有阶段事件（DELETE /api/wcs/task-stages/{taskId}）。</summary>
    public async Task<(bool Ok, int StatusCode, string Json)> DeleteTaskStageAsync(string baseUrl, string taskId)
    {
        var url = $"{baseUrl.TrimEnd('/')}/api/wcs/task-stages/{Uri.EscapeDataString(taskId)}";
        try
        {
            var resp = await _http.DeleteAsync(url);
            var body = await resp.Content.ReadAsStringAsync();
            return (resp.IsSuccessStatusCode, (int)resp.StatusCode, body);
        }
        catch (HttpRequestException ex)
        {
            return (false, 0, JsonSerializer.Serialize(new { error = ex.Message }));
        }
    }

    // ── 接驳位审批（WCS 后端管理接口 /api/wcs）──

    /// <summary>查询准入状态：自动模式 + 待确认数（GET /api/wcs/status）。</summary>
    public async Task<(bool Ok, int StatusCode, string Json)> GetAdmittanceStatusAsync(string baseUrl)
    {
        var url = $"{baseUrl.TrimEnd('/')}/api/wcs/status";
        try
        {
            var resp = await _http.GetAsync(url);
            var body = await resp.Content.ReadAsStringAsync();
            return (resp.IsSuccessStatusCode, (int)resp.StatusCode, body);
        }
        catch (HttpRequestException ex)
        {
            return (false, 0, JsonSerializer.Serialize(new { error = ex.Message }));
        }
    }

    /// <summary>查询进入申请事件列表（GET /api/wcs/events）。</summary>
    public async Task<(bool Ok, int StatusCode, string Json)> GetAdmittanceEventsAsync(string baseUrl)
    {
        var url = $"{baseUrl.TrimEnd('/')}/api/wcs/events";
        try
        {
            var resp = await _http.GetAsync(url);
            var body = await resp.Content.ReadAsStringAsync();
            return (resp.IsSuccessStatusCode, (int)resp.StatusCode, body);
        }
        catch (HttpRequestException ex)
        {
            return (false, 0, JsonSerializer.Serialize(new { error = ex.Message }));
        }
    }

    /// <summary>批准/拒绝进入申请（POST /api/wcs/decisions/{key}）。</summary>
    public async Task<(bool Ok, int StatusCode, string Json)> DecideEntryAsync(string baseUrl, string key, bool allow)
    {
        var url = $"{baseUrl.TrimEnd('/')}/api/wcs/decisions/{Uri.EscapeDataString(key)}";
        return await PostAsync(url, new { allow });
    }

    /// <summary>删除进入申请事件（DELETE /api/wcs/events/{key}）。</summary>
    public async Task<(bool Ok, int StatusCode, string Json)> DeleteEntryEventAsync(string baseUrl, string key)
    {
        var url = $"{baseUrl.TrimEnd('/')}/api/wcs/events/{Uri.EscapeDataString(key)}";
        return await DeleteAsync(url);
    }

    /// <summary>清空全部进入申请事件（DELETE /api/wcs/events）。</summary>
    public async Task<(bool Ok, int StatusCode, string Json)> ClearEntryEventsAsync(string baseUrl)
    {
        var url = $"{baseUrl.TrimEnd('/')}/api/wcs/events";
        return await DeleteAsync(url);
    }

    /// <summary>切换准入模式：auto=true 全自动放行，false 手动确认（POST /api/wcs/mode）。</summary>
    public async Task<(bool Ok, int StatusCode, string Json)> SetAdmittanceModeAsync(string baseUrl, bool auto)
    {
        var url = $"{baseUrl.TrimEnd('/')}/api/wcs/mode";
        return await PostAsync(url, new { auto });
    }

    // ── 通用方法 ──

    private async Task<(bool Ok, int StatusCode, string Json)> DeleteAsync(string url)
    {
        try
        {
            var resp = await _http.DeleteAsync(url);
            var body = await resp.Content.ReadAsStringAsync();
            return (resp.IsSuccessStatusCode, (int)resp.StatusCode, body);
        }
        catch (HttpRequestException ex) { return (false, 0, JsonSerializer.Serialize(new { error = ex.Message })); }
    }

    private async Task<(bool Ok, int StatusCode, string Json)> PostAsync<T>(string url, T payload)
    {
        try
        {
            var resp = await _http.PostAsJsonAsync(url, payload);
            var body = await resp.Content.ReadAsStringAsync();
            return (resp.IsSuccessStatusCode, (int)resp.StatusCode, body);
        }
        catch (HttpRequestException ex)
        {
            return (false, 0, JsonSerializer.Serialize(new { error = ex.Message }));
        }
    }
}
