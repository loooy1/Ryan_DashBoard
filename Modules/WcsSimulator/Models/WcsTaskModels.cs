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

/// <summary>发送给 GRCS 的任务组，对应后端 ZkWcsTaskGroupInfo。</summary>
public class WcsTaskGroup
{
    /// <summary>任务组编号（自动生成）</summary>
    public string GroupId { get; set; } = "";

    /// <summary>任务下发时间（ISO 8601 格式）</summary>
    public string MsgTime { get; set; } = "";

    /// <summary>优先级 0-1000，数字越大优先级越高</summary>
    public int PriorityCode { get; set; }

    /// <summary>仓库/场景编码，对应 GRCS 中的 SceneName</summary>
    public string Warehouse { get; set; } = "";

    /// <summary>该组内的所有任务</summary>
    public List<WcsTaskItem> Tasks { get; set; } = [];
}

/// <summary>单个任务，对应后端 ZkWcsTaskInfo。</summary>
public class WcsTaskItem
{
    /// <summary>任务唯一 ID</summary>
    public string TaskId { get; set; } = "";

    /// <summary>任务类型，如 CONTAINER_INBOUND</summary>
    public string TaskType { get; set; } = "";

    /// <summary>容器编号（托盘架或货物编码）</summary>
    public string ContainerCode { get; set; } = "";

    /// <summary>站点编码列表（CargoAreaInstance 名称或 WMS Code）</summary>
    public List<string> StationCode { get; set; } = [];

    /// <summary>区域编码（预留，暂未使用）</summary>
    public List<string> AreaCode { get; set; } = [];
}

/// <summary>GRCS 后端返回的响应。</summary>
public class WcsTaskGroupResponse
{
    public bool Success { get; set; }
    public string? Exception { get; set; }
    public string? Message { get; set; }
    public List<WcsTaskResponse>? Tasks { get; set; }
}

/// <summary>单个任务的响应信息。</summary>
public class WcsTaskResponse
{
    public string? TaskId { get; set; }
    public string? Message { get; set; }
}

/// <summary>任务台账条目（localStorage grcs_task_ledger，唯一数据源）。
/// ⚠️ 语义约定：ContainerCode 恒为【托盘号】，CargoCode 恒为【货物号】，没有的记 ""。
/// 这与下发 payload 的 ContainerCode（随任务类型装托盘或货物）无关。
/// 读侧取货物号用 CargoCode、取托盘号用 ContainerCode，切勿按 payload 习惯直接读 ContainerCode。</summary>
public class TaskLedgerEntry
{
    public string TaskId { get; set; } = "";
    public string TaskType { get; set; } = "";
    public string ContainerCode { get; set; } = "";
    public string CargoCode { get; set; } = "";
    public List<string> StationCode { get; set; } = [];
    public string Warehouse { get; set; } = "";
    public string Time { get; set; } = "";
    public bool Ok { get; set; }
    public int StatusCode { get; set; }
}

/// <summary>发送给 GRCS 的车辆任务请求体，对应后端 CreateVehicleOrderCommand。
/// 走 /api/RawOrder/ChangeFloor 接口（非 WCS 协议 task_receive）。</summary>
public class VehicleOrderRequest
{
    public DateTime CreateTime { get; set; } = DateTime.Now;
    public string SceneName { get; set; } = "";
    public string OrderType { get; set; } = "MOVE_ONLY";
    public string OrderId { get; set; } = "";
    public string OrderName { get; set; } = "";
    public string? VehicleName { get; set; } = null;
    public int Priority { get; set; }
    public List<string> StationCodes { get; set; } = [];
    public string ErrorCode { get; set; } = "";
}

/// <summary>注册所有预设任务类型，匹配 WCS 协议。</summary>
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

        // ── 其他 ──
        new("STOCK_TRANSFER", "移库", "库位→库位", 2, ["源库位","目标库位"]),
    ];
}
