using System.Text.Json;
using System.Text.Json.Serialization;

namespace GRCS.Dashboard.Modules.WcsSimulator.Models;

/// <summary>
/// 宽松 DateTime 转换器：后端 (Newtonsoft) 输出的是 "yyyy-MM-dd HH:mm:ss.fff"
/// （空格分隔，非 ISO 8601），System.Text.Json 默认不认这种格式。
/// 读取端用 DateTime.TryParse 兜底（格式不符时得 default，避免一条坏数据拖垮整批反序列化）；
/// 写入端统一输出与 GRCS 协议一致的 "yyyy-MM-dd HH:mm:ss.fff" 文本。
/// </summary>
public class FlexibleDateTimeConverter : JsonConverter<DateTime>
{
    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => DateTime.TryParse(reader.GetString(), out var dt) ? dt : default;

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.ToString("yyyy-MM-dd HH:mm:ss.fff"));
}

/// <summary>
/// WCS 后端 /api/wcs/events 返回的进入申请事件。
/// 数据流：GRCS 车辆到达接驳位前循环 POST /api/v1/station_entry_request 申请进站；
/// 手动模式下后端记录为 Pending 事件并返回 Success=false，前端轮询到此事件、
/// 经 /api/wcs/decisions/{key} 批准后，GRCS 下次重试即被放行（自动模式直接放行）。
/// 同车同站（Key 相同）的重试不会新增卡片，只刷新字段并累加 Attempts。
/// </summary>
public class EntryRequestEvent
{
    /// <summary>事件自增 ID（后端 AdmittanceService 按到达顺序分配，仅用于排序/展示）。</summary>
    public long Id { get; set; }

    /// <summary>关联键 VehicleCode@StationCode：批准/拒绝接口按此键定位事件，同车同站重试共享一张卡片。</summary>
    public string Key { get; set; } = "";

    /// <summary>申请进站的机器人编码（对应 GRCS StationEntryRequest 的 VehicleCode）。</summary>
    public string VehicleCode { get; set; } = "";

    /// <summary>申请进入的接驳位/站点编码（WCS 下发编码，含 _0/_1 后缀）。</summary>
    public string StationCode { get; set; } = "";

    /// <summary>车辆当前是否带载（true = 车上有货）。</summary>
    public bool IsLoaded { get; set; }

    /// <summary>最近一次申请时间（同键重试会刷新为最新时间）。</summary>
    [JsonConverter(typeof(FlexibleDateTimeConverter))]
    public DateTime Time { get; set; }

    /// <summary>状态：Pending 待确认 / Allowed 已放行 / Rejected 已拒绝 / Approved 已批准（等待 GRCS 下次重试领取）。</summary>
    public string Status { get; set; } = "Pending";

    /// <summary>GRCS 循环重试次数（同一申请每次重新提交 +1，同键共享）。</summary>
    public int Attempts { get; set; }
}
