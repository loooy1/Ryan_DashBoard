using GRCS.Dashboard.Modules.WcsSimulator.Models.TWD;

namespace GRCS.Dashboard.Modules.WcsSimulator.Services;

/// <summary>
/// WCS 服务接口：任务下发 + 信号模拟。
/// 当前使用 MockWcsService（开发阶段），对接后端时切换实现。
/// </summary>
public interface IWcsService
{
    // ── 任务下发 ──

    /// <summary>向 GRCS 后端发送任务组（/api/v{version}/task_receive）。</summary>
    Task<(bool Ok, int StatusCode, string Json)> SendTaskGroupAsync(
        string baseUrl, string apiVersion, WcsTaskGroup payload);

    // ── 车辆任务（GRCS 运力调度接口，非 WCS 协议）──

    /// <summary>发送车辆任务（移动/换层/充电，POST /api/RawOrder/ChangeFloor）。</summary>
    Task<(bool Ok, int StatusCode, string Json)> SendVehicleOrderAsync(
        string baseUrl, VehicleOrderRequest payload);

    // ── 库存查询 ──

    /// <summary>查询容器库存（GET /api/Cargo，支持按容器编码 / 锁定状态过滤 + 分页；场景按后端设置）。</summary>
    Task<(bool Ok, int StatusCode, string Json)> QueryCargoInventoryAsync(
        string baseUrl, string? code = null, string? scene = null, string? locked = null,
        int pageNo = 1, int pageSize = 2000);

    /// <summary>自动生成容器入库（GET /AutoContainerEnter，场景按后端设置）。</summary>
    Task<(bool Ok, int StatusCode, string Json)> AutoContainerEnterAsync(string baseUrl, string sceneName,
        string prefix = "container", int num = -1, int floor = -1, int type = 1);

    // ── 分拣信号（WCS → GRCS 出站信号）──

    /// <summary>发送分拣完成通知（/api/v{version}/container_operation_finish）。</summary>
    Task<(bool Ok, int StatusCode, string Json)> SendOperationFinishAsync(
        string baseUrl, string apiVersion, WcsOperationFinishRequest payload);

    /// <summary>发送货物到达通知（/api/v{version}/container_ready，入库容器到达输送线末端）。</summary>
    Task<(bool Ok, int StatusCode, string Json)> SendContainerReadyAsync(
        string baseUrl, string apiVersion, WcsContainerReadyRequest payload);

    /// <summary>发送货物移除通知（/api/v{version}/container_remove，出库容器离开输送线末端）。</summary>
    Task<(bool Ok, int StatusCode, string Json)> SendContainerRemoveAsync(
        string baseUrl, string apiVersion, WcsContainerRemoveRequest payload);

    // ── 任务阶段（WCS 后端管理接口 /api/wcs）──

    /// <summary>删除指定任务的所有阶段事件（DELETE /api/wcs/task-stages/{taskId}）。</summary>
    Task<(bool Ok, int StatusCode, string Json)> DeleteTaskStageAsync(string baseUrl, string taskId);

    // ── 接驳位审批（WCS 后端管理接口 /api/wcs）──

    /// <summary>批准/拒绝进入申请（POST /api/wcs/decisions/{key}）。</summary>
    Task<(bool Ok, int StatusCode, string Json)> DecideEntryAsync(string baseUrl, string key, bool allow);
    /// <summary>删除进入申请事件（DELETE /api/wcs/events/{key}）。</summary>
    Task<(bool Ok, int StatusCode, string Json)> DeleteEntryEventAsync(string baseUrl, string key);
    /// <summary>清空全部进入申请事件（DELETE /api/wcs/events）。</summary>
    Task<(bool Ok, int StatusCode, string Json)> ClearEntryEventsAsync(string baseUrl);
}
