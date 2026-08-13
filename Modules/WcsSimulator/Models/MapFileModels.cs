using System.Text.Json;
using System.Text.Json.Serialization;

namespace GRCS.Dashboard.Modules.WcsSimulator.Models;

/// <summary>map.json 顶层结构（对应 GRCS GetMap 接口导出的 feMap 数据）。</summary>
public class MapFileData
{
    public string? Floor { get; set; }
    public string? Scene { get; set; }

    /// <summary>站点字典：键 = 站点 mark。</summary>
    public Dictionary<string, MapStation>? Stations { get; set; }

    /// <summary>连线字典：键 = 连线 id。</summary>
    public Dictionary<string, MapPath>? Paths { get; set; }
}

/// <summary>单个站点，字段对应 map.json 的 station 条目。</summary>
public class MapStation
{
    public string? Mark { get; set; }
    public int StationType { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }
    public int Floor { get; set; }
    public string? QrCode { get; set; }
    public string? StationTag { get; set; }
    public bool StaEnable { get; set; }
    public List<string>? CargoAreas { get; set; }
    public bool AllowTurn { get; set; }
    public bool AllowAvoid { get; set; }
    public bool AllowStop { get; set; }
    public int SupportLoadState { get; set; }
    public string? ColumnName { get; set; }
    public int Depth { get; set; }
    public double CargoPalletAngle { get; set; }
    public List<string>? BindPickStations { get; set; }
    public List<string>? BindStandbyStations { get; set; }
    public List<MapStationDevice>? Devices { get; set; }
}

/// <summary>站点绑定的设备（输送线、入库口等），对应 map.json devices 数组元素。</summary>
public class MapStationDevice
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public string? DeviceType { get; set; }
    public string? DeviceTemplateName { get; set; }
    public string? StationMark { get; set; }
    public string? StationScene { get; set; }
    public JsonElement? Admittance { get; set; }
    public JsonElement? Perform { get; set; }
    public JsonElement? FinishNotice { get; set; }
    public List<JsonElement>? AdmittanceArgs { get; set; }
    public List<JsonElement>? PerformArgs { get; set; }
    public List<JsonElement>? FinishNoticeArgs { get; set; }
}

/// <summary>地图连线，字段对应 map.json 的 path 条目。</summary>
public class MapPath
{
    public string? Id { get; set; }
    public string? Type { get; set; }
    public string? StartName { get; set; }
    public string? EndName { get; set; }
    public int Floor { get; set; }
    public bool IsVirtual { get; set; }
    public bool RouteEnable { get; set; }
}

/// <summary>持久化到 localStorage 的精简站点信息（地图信息页整理后保存，供其他页面使用）。</summary>
public class MapStationLite
{
    public string Mark { get; set; } = "";
    public int StationType { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public int Floor { get; set; }
    public bool StaEnable { get; set; }
    public List<string> CargoAreas { get; set; } = [];
    public int SupportLoadState { get; set; }
    public bool AllowTurn { get; set; }
    public bool AllowAvoid { get; set; }
    public bool AllowStop { get; set; }
}

/// <summary>localStorage 中的地图站点缓存结构。</summary>
public class MapStationCache
{
    public string SavedAt { get; set; } = "";
    public int PathsCount { get; set; }
    public List<MapStationLite> Stations { get; set; } = [];

    /// <summary>地图信息页的筛选状态（切走再回来时恢复界面）。</summary>
    public MapReaderFilterState? Filter { get; set; }
}

/// <summary>地图信息页筛选状态。</summary>
public class MapReaderFilterState
{
    public int TypeFilter { get; set; }
    public string Search { get; set; } = "";
    public string EnableFilter { get; set; } = "";
}

/// <summary>站点类型位标志（对照 GRCS StationType 枚举），支持组合，如 5 = 道路+储位。</summary>
public static class MapStationTypeBits
{
    public const int NormalRoad = 0b_0000_0001;        // 1   普通道路
    public const int HighWay = 0b_0000_0010;           // 2   高速路
    public const int StorageLocation = 0b_0000_0100;   // 4   储位
    public const int TransferPoint = 0b_0000_1000;     // 8   接驳位
    public const int Parking = 0b_0001_0000;           // 16  停车位
    public const int Charging = 0b_0010_0000;          // 32  充电点
    public const int PickingStation = 0b_0100_0000;    // 64  分拣点
    public const int PeopleStation = 0b_1000_0000;     // 128 人工拣选台
    public const int Elevator = 0b_0001_0000_0000;     // 256 电梯点
    public const int Other = 0b_0010_0000_0000;        // 512 其他

    /// <summary>可筛选/展示的类型位列表（位值 + 中文名）。</summary>
    public static readonly (int Bit, string Name)[] All =
    [
        (NormalRoad, "普通道路"),
        (HighWay, "高速路"),
        (StorageLocation, "储位"),
        (TransferPoint, "接驳位"),
        (Parking, "停车位"),
        (Charging, "充电点"),
        (PickingStation, "分拣点"),
        (PeopleStation, "人工拣选台"),
        (Elevator, "电梯点"),
        (Other, "其他"),
    ];

    /// <summary>解码一个站点的类型位，返回其中文名列表（如 5 → ["普通道路", "储位"]）。</summary>
    public static List<string> DecodeNames(int type)
    {
        var names = new List<string>();
        foreach (var (bit, name) in All)
        {
            if ((type & bit) != 0) names.Add(name);
        }
        if (names.Count == 0) names.Add($"未配置({type})");
        return names;
    }

    // 编码转换方法（ToWcsCode）已移至 Extensions/MapStationTypeExtensions.cs，本类只保留类型位定义。
}
