namespace GRCS.Dashboard.Modules.WcsSimulator.Models;

/// <summary>
/// 分拣完成通知请求体（WCS → GRCS，POST /api/v{version}/container_operation_finish）。
/// 容器拣选完成后通知 GRCS 该容器可以离开分拣台；未指定推荐目的地则默认回原库位。
/// </summary>
public class WcsOperationFinishRequest
{
    public DateTime MsgTime { get; set; } = DateTime.Now;
    public string Warehouse { get; set; } = "";

    /// <summary>若未指定任务号，则该回库任务通过原任务接口获得。</summary>
    public string TaskId { get; set; } = "";

    /// <summary>容器编号。</summary>
    public string ContainerCode { get; set; } = "";

    /// <summary>容器是否出场：true 代表不需要执行后续回库任务。</summary>
    public bool RemoveContainer { get; set; }

    /// <summary>推荐目的地：若未指定该值，则默认该容器回到原库位。</summary>
    public string StationCode { get; set; } = "";

    /// <summary>推荐区域编号：若指定了区域编码则 GRCS 按区域位置自行确定实际放货位置。</summary>
    public string AreaCode { get; set; } = "";
}

/// <summary>
/// 货物到达通知请求体（WCS → RCS，POST /api/v{version}/container_ready）。
/// 入库容器到达输送线末端时通知 RCS 容器已就位，等待入场。
/// </summary>
public class WcsContainerReadyRequest
{
    public DateTime MsgTime { get; set; } = DateTime.Now;
    public string Warehouse { get; set; } = "";

    /// <summary>任务编号。</summary>
    public string TaskId { get; set; } = "";

    /// <summary>容器编号。</summary>
    public string ContainerCode { get; set; } = "";

    /// <summary>工作站编码，不代表库位编码，需要精确到库位编码。</summary>
    public string StationCode { get; set; } = "";
}

/// <summary>
/// 货物到达卡片：由历史任务中的 CARGO_CARRY_INBOUND 任务自动生成，
/// 等待手动确认或自动模式全确认后 POST container_ready 通知 RCS。
/// </summary>
public class ArrivalCard
{
    public string TaskId { get; set; } = "";

    /// <summary>输送线编号（任务接驳位 StationCode[0]，如 CONVEYOR_IN_01）。</summary>
    public string ConveyorCode { get; set; } = "";

    public string ContainerCode { get; set; } = "";

    public string Warehouse { get; set; } = "";

    /// <summary>任务下发时间（历史记录保存的时间文本）。</summary>
    public string Time { get; set; } = "";

    /// <summary>是否已确认（已 POST container_ready）。</summary>
    public bool Confirmed { get; set; }
}

/// <summary>
/// 货物移除通知请求体（WCS → RCS，POST /api/v{version}/container_remove）。
/// 出库容器离开输送线末端时通知 RCS 容器已移除，任务完成。
/// </summary>
public class WcsContainerRemoveRequest
{
    public DateTime MsgTime { get; set; } = DateTime.Now;
    public string Warehouse { get; set; } = "";

    /// <summary>容器编号。</summary>
    public string ContainerCode { get; set; } = "";

    /// <summary>工作站编码，不代表库位编码，需要精确到库位编码。</summary>
    public string StationCode { get; set; } = "";
}

/// <summary>
/// 货物移除卡片：由历史任务中的 CONTAINER_CARRY_OUTBOUND 任务自动生成，
/// 等待手动确认或自动模式全确认后 POST container_remove 通知 RCS。
/// </summary>
public class RemovalCard
{
    public string TaskId { get; set; } = "";

    /// <summary>出库接驳位编号（任务 StationCode[0]）。</summary>
    public string ConveyorCode { get; set; } = "";

    public string ContainerCode { get; set; } = "";

    public string Warehouse { get; set; } = "";

    /// <summary>任务下发时间（历史记录保存的时间文本）。</summary>
    public string Time { get; set; } = "";

    /// <summary>是否已确认（已 POST container_remove）。</summary>
    public bool Confirmed { get; set; }
}

/// <summary>分拣完成卡片：由分拣任务完成（FINISHED）事件自动生成，等待/已完成发送。</summary>
public class SortingCard
{
    public string TaskId { get; set; } = "";
    public string TaskType { get; set; } = "";            // 任务类型（分拣类）
    public string ContainerCode { get; set; } = "";
    public string Warehouse { get; set; } = "";
    public string PickStation { get; set; } = "";        // 分拣台位置（事件中的 StationCode）
    public DateTime FoundAt { get; set; }                 // 卡片生成时间
    public DateTime? SendTime { get; set; }               // 发送时间

    /// <summary>待发送 / 已发送 / 发送失败。</summary>
    public string Status { get; set; } = "Pending";

    // 发送参数（可编辑）
    public bool RemoveContainer { get; set; }              // 容器出场
    public string DestStation { get; set; } = "";          // 推荐目的地（留空 = 回原库位）
    public string DestArea { get; set; } = "";             // 推荐区域
    public string ReturnTaskId { get; set; } = "";         // 回库任务号（留空=使用分拣任务号）

    public string ResponseText { get; set; } = "";         // GRCS 响应
}
