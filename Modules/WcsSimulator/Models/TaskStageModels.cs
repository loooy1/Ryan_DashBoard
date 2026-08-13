using System.Text.Json.Serialization;

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
