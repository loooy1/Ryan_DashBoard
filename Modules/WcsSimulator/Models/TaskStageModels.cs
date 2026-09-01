using System.Text.Json.Serialization;
using GRCS.Dashboard.Modules.WcsSimulator.Models;

namespace GRCS.Dashboard.Modules.WcsSimulator.Models;

/// <summary>
/// WCS 后端 /api/wcs/task-stages 返回的任务阶段变化事件。
/// 数据流：GRCS 任务推进时 POST /api/v1/task_stage_change 上报阶段
/// （START / LOAD_FINISH / FINISHED），后端记录事件；前端 TaskStageHub 统一轮询
/// （增量轮询用 sinceId 比对 Id）并分发——任务阶段页展示、信号交互页据此生成分拣完成卡片。
/// </summary>
public class StageChangeEvent
{
    /// <summary>事件自增 ID（后端 TaskStageService 分配，增量轮询 sinceId 用它比对）。</summary>
    public long Id { get; set; }

    /// <summary>任务 ID（GRCS 下发时的 TaskId）。</summary>
    public string TaskId { get; set; } = "";

    /// <summary>任务类型（前端扩展字段：后端阶段事件不含此字段，任务阶段页从台账按 TaskId 回填，仅展示用）。</summary>
    public string TaskType { get; set; } = "";

    /// <summary>仓库/场景编码。</summary>
    public string Warehouse { get; set; } = "";

    /// <summary>事件发生站点（FINISHED 时 = 容器实际放货位置，分拣卡片取它定位分拣台）。</summary>
    public string StationCode { get; set; } = "";

    /// <summary>容器/货物编码（"" = 无货，"Unknown" = 有货但未扫到码）。</summary>
    public string ContainerCode { get; set; } = "";

    /// <summary>阶段：START 开始 / LOAD_FINISH 装货完成 / FINISHED 任务结束。</summary>
    public string Stage { get; set; } = "";

    /// <summary>事件时间（后端 Newtonsoft "yyyy-MM-dd HH:mm:ss.fff" 格式，宽松转换防解析失败）。</summary>
    [JsonConverter(typeof(FlexibleDateTimeConverter))]
    public DateTime Time { get; set; }
}

/// <summary>
/// 合并表记录（后端 /hubs/task-stages SignalR 推送的 task_records 全表条目）。
/// 一个 TaskId 的一个状态快照：stage = CREATED（WCS 下发时写，含台账字段）/
/// START / LOAD_FINISH / FINISHED（GRCS 阶段回调）。TaskStageHub 全表缓存，消费方按 stage 筛选。
/// 台账字段（TaskType / RouteCodes / CargoCode / Ok / StatusCode）仅创建行有值；
/// StationCode 仅阶段行有值（GRCS 上报的当前站点）。
/// </summary>
public class TaskRecord
{
    /// <summary>记录自增 ID（后端 SQLite 分配，创建行与阶段行共享 Id 空间）。</summary>
    public long Id { get; set; }

    /// <summary>任务 ID（GRCS 下发时的 TaskId，段1/段2 各自独立生命周期）。</summary>
    public string TaskId { get; set; } = "";

    /// <summary>状态快照：CREATED / START / LOAD_FINISH / FINISHED。</summary>
    public string Stage { get; set; } = "";

    /// <summary>创建时刻（创建行）或阶段到达时刻（阶段行）。</summary>
    [JsonConverter(typeof(FlexibleDateTimeConverter))]
    public DateTime Time { get; set; }

    /// <summary>仓库/场景编码。</summary>
    public string Warehouse { get; set; } = "";

    /// <summary>托盘号（创建行与阶段行都有值；语义约定见 TaskLedgerEntry）。</summary>
    public string ContainerCode { get; set; } = "";

    /// <summary>货物号（创建行填，阶段行留空）。</summary>
    public string CargoCode { get; set; } = "";

    /// <summary>任务类型（创建行填；前端阶段事件 TaskType 字段也由此回填）。</summary>
    public string TaskType { get; set; } = "";

    /// <summary>站点对（创建行填，任务路径；原台账 station_code JSON）。</summary>
    public List<string> RouteCodes { get; set; } = [];

    /// <summary>当前站点（阶段行填，FINISHED 时 = 容器实际放货位置）。</summary>
    public string StationCode { get; set; } = "";

    /// <summary>HTTP 受理是否成功（创建行填）。</summary>
    public bool Ok { get; set; }

    /// <summary>HTTP 状态码（创建行填）。</summary>
    public int StatusCode { get; set; }

    public bool IsCreated => string.Equals(Stage, "CREATED", StringComparison.OrdinalIgnoreCase);

    /// <summary>投影为台账条目（卡片骨架/看板列表展示用，形状与旧 TaskLedgerEntry 一致）。</summary>
    public TaskLedgerEntry ToLedgerEntry() => new()
    {
        TaskId = TaskId,
        TaskType = TaskType,
        ContainerCode = ContainerCode,
        CargoCode = CargoCode,
        StationCode = RouteCodes,
        Warehouse = Warehouse,
        Time = Time.ToString("O"),
        Ok = Ok,
        StatusCode = StatusCode,
    };

    /// <summary>投影为阶段事件（时间线/分拣卡片用，形状与旧 StageChangeEvent 一致）。</summary>
    public StageChangeEvent ToStageEvent() => new()
    {
        Id = Id,
        TaskId = TaskId,
        TaskType = TaskType,
        Warehouse = Warehouse,
        StationCode = StationCode,
        ContainerCode = ContainerCode,
        Stage = Stage,
        Time = Time,
    };
}
