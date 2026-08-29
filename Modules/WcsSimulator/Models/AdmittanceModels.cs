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

/// <summary>可空宽松 DateTime 转换器（后端 Newtonsoft 输出 "yyyy-MM-dd HH:mm:ss.fff"，null 原样透传）。</summary>
public class FlexibleNullableDateTimeConverter : JsonConverter<DateTime?>
{
    public override DateTime? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => reader.TokenType == JsonTokenType.Null ? null
            : DateTime.TryParse(reader.GetString(), out var dt) ? dt : null;

    public override void Write(Utf8JsonWriter writer, DateTime? value, JsonSerializerOptions options)
    {
        if (value.HasValue) writer.WriteStringValue(value.Value.ToString("yyyy-MM-dd HH:mm:ss.fff"));
        else writer.WriteNullValue();
    }
}
