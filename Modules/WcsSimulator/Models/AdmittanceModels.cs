using System.Text.Json;
using System.Text.Json.Serialization;

namespace GRCS.Dashboard.Modules.WcsSimulator.Models;

/// <summary>
/// 宽松 DateTime 转换器：后端 (Newtonsoft) 输出的是 "yyyy-MM-dd HH:mm:ss.fff"
/// （空格分隔，非 ISO 8601），System.Text.Json 默认不认这种格式。
/// </summary>
public class FlexibleDateTimeConverter : JsonConverter<DateTime>
{
    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => DateTime.TryParse(reader.GetString(), out var dt) ? dt : default;

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.ToString("yyyy-MM-dd HH:mm:ss.fff"));
}

/// <summary>WCS 后端 /api/wcs/events 返回的进入申请事件。</summary>
public class EntryRequestEvent
{
    public long Id { get; set; }
    public string Key { get; set; } = "";
    public string VehicleCode { get; set; } = "";
    public string StationCode { get; set; } = "";
    public bool IsLoaded { get; set; }
    [JsonConverter(typeof(FlexibleDateTimeConverter))]
    public DateTime Time { get; set; }
    public string Status { get; set; } = "Pending";
    public int Attempts { get; set; }
}
