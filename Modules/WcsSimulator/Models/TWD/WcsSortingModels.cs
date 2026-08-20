namespace GRCS.Dashboard.Modules.WcsSimulator.Models.TWD;

/// <summary>
/// 分拣完成通知请求体（WCS → GRCS，POST /api/v{version}/container_operation_finish）。
/// 容器拣选完成后通知 GRCS 该容器可以离开分拣台；未指定推荐目的地则默认回原库位。
/// 数据流：分拣任务上报 FINISHED 阶段事件 → 信号交互页生成 SortingCard（手动发送）
/// 或 SignalAutoService 自动发送（自动模式），最终组装本请求 POST 到 GRCS，
/// GRCS 据此生成回库任务（RemoveContainer=true 时不生成）。
/// </summary>
public class WcsOperationFinishRequest
{
    /// <summary>消息时间（协议固定字段）。</summary>
    public DateTime MsgTime { get; set; } = DateTime.Now;

    /// <summary>仓库/场景编码。</summary>
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
/// 入库容器到达输送线末端时通知 RCS 容器已就位，等待入场（随后 RCS 侧车辆会发
/// station_entry_request 申请进站，手动模式下在准入卡片页批准）。
/// 本模拟器中该请求直接 POST 到 GRCS 后端（GRCS 兼作 RCS 角色）。
/// </summary>
public class WcsContainerReadyRequest
{
    /// <summary>消息时间（协议固定字段）。</summary>
    public DateTime MsgTime { get; set; } = DateTime.Now;

    /// <summary>仓库/场景编码。</summary>
    public string Warehouse { get; set; } = "";

    /// <summary>任务编号（到达卡片对应的段1/段2 任务 ID）。</summary>
    public string TaskId { get; set; } = "";

    /// <summary>容器编号。</summary>
    public string ContainerCode { get; set; } = "";

    /// <summary>工作站编码，不代表库位编码，需要精确到库位编码。</summary>
    public string StationCode { get; set; } = "";
}

/// <summary>
/// 货物到达卡片：由历史任务中的 CARGO_CARRY_INBOUND 任务自动生成，
/// 等待手动确认或自动模式全确认后 POST container_ready 通知 RCS。
/// 数据流：信号交互页从任务台账筛选入库任务（自动模式取段1、手动模式取段2，
/// 且要求段1 已完成/段2 已下发）生成卡片；确认集合持久化在后端 SQLite
/// workflow_state 表（kind=arrival），由前端 AutomationHub 每秒轮询同步，跨标签页一致。
/// </summary>
public class ArrivalCard
{
    /// <summary>对应台账任务 ID（自动模式 = 段1 TaskId，手动模式 = 段2 TaskId）。</summary>
    public string TaskId { get; set; } = "";

    /// <summary>输送线编号（任务接驳位 StationCode[0]，如 CONVEYOR_IN_01）。</summary>
    public string ConveyorCode { get; set; } = "";

    /// <summary>货物码（自动模式经 CargoCodeService 按段1 TaskId 生成，手动模式取台账 CargoCode）。</summary>
    public string ContainerCode { get; set; } = "";

    /// <summary>仓库/场景编码。</summary>
    public string Warehouse { get; set; } = "";

    /// <summary>任务下发时间（历史记录保存的时间文本）。</summary>
    public string Time { get; set; } = "";

    /// <summary>申请时间：GRCS 任务完成（FINISHED 事件）时刻，卡片由此生成。</summary>
    public DateTime? AppliedAt { get; set; }

    /// <summary>信号下发时间：container_ready 确认发送时刻（workflow_state 落库时间，未确认为 null）。</summary>
    public DateTime? SendAt { get; set; }

    /// <summary>是否已确认（已 POST container_ready）。</summary>
    public bool Confirmed { get; set; }
}

/// <summary>
/// 货物移除通知请求体（WCS → RCS，POST /api/v{version}/container_remove）。
/// 出库容器离开输送线末端时通知 RCS 容器已移除，任务完成。
/// 数据流：与到达卡片对称——出库任务确认后发送本请求，RCS 据此闭环出库任务。
/// 本模拟器中该请求直接 POST 到 GRCS 后端（GRCS 兼作 RCS 角色）。
/// </summary>
public class WcsContainerRemoveRequest
{
    /// <summary>消息时间（协议固定字段）。</summary>
    public DateTime MsgTime { get; set; } = DateTime.Now;

    /// <summary>仓库/场景编码。</summary>
    public string Warehouse { get; set; } = "";

    /// <summary>容器编号。</summary>
    public string ContainerCode { get; set; } = "";

    /// <summary>工作站编码，不代表库位编码，需要精确到库位编码。</summary>
    public string StationCode { get; set; } = "";
}

/// <summary>
/// 货物移除卡片：由历史任务中的 CONTAINER_CARRY_OUTBOUND 任务自动生成，
/// 等待手动确认或自动模式全确认后 POST container_remove 通知 RCS。
/// 数据流：信号交互页从任务台账筛选出库任务（自动模式取出库段1 CARGO_CARRY_OUTBOUND、
/// 手动模式取出库段2 CONTAINER_CARRY_OUTBOUND）生成卡片；确认集合持久化在后端 SQLite
/// workflow_state 表（kind=removal），由前端 AutomationHub 每秒轮询同步，跨标签页一致。
/// </summary>
public class RemovalCard
{
    /// <summary>对应台账任务 ID（自动模式 = 出库段1，手动模式 = 出库段2）。</summary>
    public string TaskId { get; set; } = "";

    /// <summary>出库接驳位编号（任务 StationCode[0]）。</summary>
    public string ConveyorCode { get; set; } = "";

    /// <summary>容器号（优先取货物号 CargoCode，没有则回退托盘号 ContainerCode）。</summary>
    public string ContainerCode { get; set; } = "";

    /// <summary>仓库/场景编码。</summary>
    public string Warehouse { get; set; } = "";

    /// <summary>任务下发时间（历史记录保存的时间文本）。</summary>
    public string Time { get; set; } = "";

    /// <summary>申请时间：GRCS 任务完成（FINISHED 事件）时刻，卡片由此生成。</summary>
    public DateTime? AppliedAt { get; set; }

    /// <summary>信号下发时间：container_remove 确认发送时刻（workflow_state 落库时间，未确认为 null）。</summary>
    public DateTime? SendAt { get; set; }

    /// <summary>是否已确认（已 POST container_remove）。</summary>
    public bool Confirmed { get; set; }
}

/// <summary>
/// 分拣完成卡片：由分拣任务完成（FINISHED）事件自动生成，等待/已完成发送。
/// 数据流：TaskStageHub 轮询到的阶段事件里 Stage == "FINISHED" 且站点为分拣点/
/// 人工拣选台 → 信号交互页生成卡片（手动发送）或 SignalAutoService 自动发送
/// container_operation_finish；发送参数可在界面上编辑后重发。
/// </summary>
public class SortingCard
{
    /// <summary>完成的分拣任务 ID（FINISHED 阶段事件的 TaskId）。</summary>
    public string TaskId { get; set; } = "";
    public string TaskType { get; set; } = "";            // 任务类型（分拣类）：由完成事件站点的类型位解码而来（如 "分拣点"）
    public string ContainerCode { get; set; } = "";       // 容器/货物编码（FINISHED 事件的 ContainerCode，"Unknown" = 有货未扫到码）
    public string Warehouse { get; set; } = "";           // 仓库/场景编码
    public string PickStation { get; set; } = "";         // 分拣台位置（事件中的 StationCode）
    public DateTime FoundAt { get; set; }                 // 卡片生成时间（FINISHED 事件时间）
    public DateTime? SendTime { get; set; }               // 发送时间（container_operation_finish 实际 POST 的时刻）

    /// <summary>待发送 / 已发送 / 发送失败。</summary>
    public string Status { get; set; } = "Pending";

    // 发送参数（可编辑）
    public bool RemoveContainer { get; set; }              // 容器出场（true = GRCS 不再生成回库任务）
    public string DestStation { get; set; } = "";          // 推荐目的地（留空 = 回原库位）
    public string DestArea { get; set; } = "";             // 推荐区域（指定后 GRCS 按区域自行确定放货位置）
    public string ReturnTaskId { get; set; } = "";         // 回库任务号（留空=使用分拣任务号；自动生成时 = TaskId + "_R"）

    public string ResponseText { get; set; } = "";         // GRCS 响应（发送结果回显）
}
