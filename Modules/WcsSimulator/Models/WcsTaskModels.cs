namespace GRCS.Dashboard.Modules.WcsSimulator.Models;

/// <summary>预设的任务类型信息。</summary>
/// <param name="Value">任务类型枚举值，对应 GRCS 后端 RawOrderType</param>
/// <param name="Label">中文显示名称</param>
/// <param name="Description">简短描述</param>
/// <param name="StationCount">需要的站点数量</param>
/// <param name="StationLabels">每个站点的中文标签</param>
public record TaskTypeInfo(
    string Value,
    string Label,
    string Description,
    int StationCount,
    string[] StationLabels);

/// <summary>
/// 发送给 GRCS 的任务组，对应后端 ZkWcsTaskGroupInfo。
/// 数据流：任务下发页/自动服务组装任务组，POST /api/v{version}/task_receive；
/// 两段式任务（入库/出库）按段依次下发——段1 先发一组，等货物信号确认后再发段2 一组。
/// </summary>
public class WcsTaskGroup
{
    /// <summary>任务组编号（自动生成，SimAuto_ / SimManual_ 前缀区分自动/手动来源）</summary>
    public string GroupId { get; set; } = "";

    /// <summary>任务下发时间（ISO 8601 格式）</summary>
    public string MsgTime { get; set; } = "";

    /// <summary>优先级 0-1000，数字越大优先级越高</summary>
    public int PriorityCode { get; set; }

    /// <summary>仓库/场景编码，对应 GRCS 中的 SceneName</summary>
    public string Warehouse { get; set; } = "";

    /// <summary>该组内的所有任务（一组通常只装一段任务）</summary>
    public List<WcsTaskItem> Tasks { get; set; } = [];
}

/// <summary>单个任务，对应后端 ZkWcsTaskInfo。</summary>
public class WcsTaskItem
{
    /// <summary>任务唯一 ID（全链路按它关联：阶段事件、台账、信号卡片）</summary>
    public string TaskId { get; set; } = "";

    /// <summary>任务类型，对应 GRCS 后端 RawOrderType，如 CONTAINER_INBOUND</summary>
    public string TaskType { get; set; } = "";

    /// <summary>
    /// 容器编号（托盘架或货物编码）：内容随任务类型变化——
    /// 入库段1/出库段2 装托盘号，入库段2/出库段1/分拣装货物号。
    /// ⚠️ 与台账语义不同：台账里 ContainerCode 恒为托盘号、CargoCode 恒为货物号。
    /// </summary>
    public string ContainerCode { get; set; } = "";

    /// <summary>站点编码列表（CargoAreaInstance 名称或 WMS Code），顺序即任务路径，需带 _0/_1 后缀（见 MapStationTypeExtensions）</summary>
    public List<string> StationCode { get; set; } = [];

    /// <summary>区域编码（预留，暂未使用）</summary>
    public List<string> AreaCode { get; set; } = [];
}

/// <summary>GRCS 后端 task_receive 接口返回的响应。</summary>
public class WcsTaskGroupResponse
{
    /// <summary>任务组整体受理结果。</summary>
    public bool Success { get; set; }

    /// <summary>后端异常信息（非空 = 受理失败，展示给用户定位原因）。</summary>
    public string? Exception { get; set; }

    /// <summary>后端返回的消息文本。</summary>
    public string? Message { get; set; }

    /// <summary>逐任务响应列表（部分任务被拒时逐个说明原因）。</summary>
    public List<WcsTaskResponse>? Tasks { get; set; }
}

/// <summary>单个任务的响应信息。</summary>
public class WcsTaskResponse
{
    /// <summary>对应下发的任务 ID。</summary>
    public string? TaskId { get; set; }

    /// <summary>该任务的处理消息。</summary>
    public string? Message { get; set; }
}

/// <summary>任务台账条目（localStorage grcs_task_ledger，唯一数据源）。
/// ⚠️ 语义约定：ContainerCode 恒为【托盘号】，CargoCode 恒为【货物号】，没有的记 ""。
/// 这与下发 payload 的 ContainerCode（随任务类型装托盘或货物）无关。
/// 读侧取货物号用 CargoCode、取托盘号用 ContainerCode，切勿按 payload 习惯直接读 ContainerCode。</summary>
public class TaskLedgerEntry
{
    /// <summary>任务唯一 ID（SimAuto_ 前缀 = 自动任务，SimManual_ 前缀 = 手动任务）。</summary>
    public string TaskId { get; set; } = "";

    /// <summary>任务类型（RawOrderType 值，如 CARGO_CARRY_INBOUND）。</summary>
    public string TaskType { get; set; } = "";

    /// <summary>托盘号（语义约定见类注释：恒为托盘号，与下发 payload 的 ContainerCode 无关）。</summary>
    public string ContainerCode { get; set; } = "";

    /// <summary>货物号（没有的记 ""；读侧取货物号一律用本字段）。</summary>
    public string CargoCode { get; set; } = "";

    /// <summary>站点编码列表（任务路径，按序）。</summary>
    public List<string> StationCode { get; set; } = [];

    /// <summary>仓库/场景编码（= SceneName）。</summary>
    public string Warehouse { get; set; } = "";

    /// <summary>下发时间（ISO 8601 文本）。</summary>
    public string Time { get; set; } = "";

    /// <summary>HTTP 受理是否成功。</summary>
    public bool Ok { get; set; }

    /// <summary>HTTP 状态码（失败时定位原因）。</summary>
    public int StatusCode { get; set; }
}

/// <summary>
/// 发送给 GRCS 的车辆任务请求体，对应后端 CreateVehicleOrderCommand。
/// 走 /api/RawOrder/ChangeFloor 接口（非 WCS 协议 task_receive）。
/// 用途：车辆调度页直接给机器人下发移动/换层/充电任务（不与容器/信号流程绑定）。
/// </summary>
public class VehicleOrderRequest
{
    /// <summary>创建时间。</summary>
    public DateTime CreateTime { get; set; } = DateTime.Now;

    /// <summary>场景名（= Warehouse）。</summary>
    public string SceneName { get; set; } = "";

    /// <summary>订单类型：MOVE_ONLY 移动 / CHANGE_FLOOR 换层 / CHARGE 充电。</summary>
    public string OrderType { get; set; } = "MOVE_ONLY";

    /// <summary>订单 ID（自动生成）。</summary>
    public string OrderId { get; set; } = "";

    /// <summary>订单名称（界面展示用）。</summary>
    public string OrderName { get; set; } = "";

    /// <summary>指定车辆（null = 交给 GRCS 自动调度）。</summary>
    public string? VehicleName { get; set; } = null;

    /// <summary>优先级。</summary>
    public int Priority { get; set; }

    /// <summary>目标站点序列（移动任务的路径/终点，已含 WCS 编码后缀）。</summary>
    public List<string> StationCodes { get; set; } = [];

    /// <summary>错误码（协议要求字段，正常下发固定空串）。</summary>
    public string ErrorCode { get; set; } = "";
}

/// <summary>
/// 注册所有预设任务类型，匹配 WCS 协议（task_receive 的 TaskType 字段）。
/// 两段式任务（入库/出库）需按段依次下发：段1 先发，等货物到达/移除信号确认后
/// 前端再下发段2（"货先走、托后回"）；分拣任务段2 由 RCS 自生成，WCS 只发段1。
/// </summary>
public static class TaskTypeRegistry
{
    public static readonly TaskTypeInfo[] All =
    [
        // ── 入库（两段任务）──
        new("CONTAINER_CARRY_INBOUND", "入库-段1：搬运空托", "AMR库→输送线",   2, ["空托库位","接驳位"]),
        new("CARGO_CARRY_INBOUND",     "入库-段2：搬运带载", "输送线→AMR库",   2, ["接驳位","目标库位"]),

        // ── 出库（两段任务）──
        new("CARGO_CARRY_OUTBOUND",     "出库-段1：搬运带载", "AMR库→输送线",   2, ["货物库位","出库接驳位"]),
        new("CONTAINER_CARRY_OUTBOUND", "出库-段2：搬运空托", "输送线→AMR库",   2, ["出库接驳位","空托回库位"]),

        // ── 库内分拣 ──
        new("SORTING",       "分拣-段1：搬运带载", "AMR库→分拣台",  2, ["货物库位","分拣台"]),
        // 段2 由 RCS 自生成，WCS 不需要发
        new("SORTING_RETURN", "分拣-回库", "分拣台→AMR库", 1, ["分拣台","回库库位"]),

        // ── 其他 ──
        new("STOCK_TRANSFER", "移库", "库位→库位", 2, ["源库位","目标库位"]),
    ];
}
