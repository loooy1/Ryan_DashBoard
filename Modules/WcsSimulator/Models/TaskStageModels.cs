using System.Text.Json.Serialization;

namespace GRCS.Dashboard.Modules.WcsSimulator.Models;

/// <summary>WCS 后端 /api/wcs/task-stages 返回的任务阶段变化事件。</summary>
public class StageChangeEvent
{
    public long Id { get; set; }
    public string TaskId { get; set; } = "";
    public string TaskType { get; set; } = "";
    public string Warehouse { get; set; } = "";
    public string StationCode { get; set; } = "";
    public string ContainerCode { get; set; } = "";
    public string Stage { get; set; } = "";
    [JsonConverter(typeof(FlexibleDateTimeConverter))]
    public DateTime Time { get; set; }
}
