using System.Text.Json;

namespace GRCS.Dashboard.Modules.WcsSimulator.Models;

/// <summary>
/// map.json 顶层结构（对应 GRCS GetMap 接口导出的 feMap 数据）。
/// 数据流：地图信息页 GET /api/Map/GetMap?sceneName=&getTypes=feMap 下载 zip、
/// 解出 map.json 反序列化为本类型展示；整理出的 MapStationLite 列表随后写
/// localStorage（grcs_map_stations），供任务下发/信号交互/车辆调度等页面共享。
/// </summary>
public class MapFileData
{
    /// <summary>默认楼层。</summary>
    public string? Floor { get; set; }

    /// <summary>场景名（= 下发任务时的 Warehouse 参数）。</summary>
    public string? Scene { get; set; }

    /// <summary>站点字典：键 = 站点 mark。</summary>
    public Dictionary<string, MapStation>? Stations { get; set; }

    /// <summary>连线字典：键 = 连线 id。</summary>
    public Dictionary<string, MapPath>? Paths { get; set; }
}

/// <summary>单个站点，字段对应 map.json 的 station 条目。</summary>
public class MapStation
{
    /// <summary>站点裸编码（GRCS 内部 Mark，不含 _0/_1 后缀）。</summary>
    public string? Mark { get; set; }

    /// <summary>站点类型位组合（对照 MapStationTypeBits，如 5 = 普通道路+储位）。</summary>
    public int StationType { get; set; }

    /// <summary>地图 X 坐标（车辆调度页绘制站点用）。</summary>
    public double X { get; set; }

    /// <summary>地图 Y 坐标。</summary>
    public double Y { get; set; }

    /// <summary>地图 Z 坐标（高度/层）。</summary>
    public double Z { get; set; }

    /// <summary>站点所在楼层。</summary>
    public int Floor { get; set; }

    /// <summary>站点二维码/标识（可空）。</summary>
    public string? QrCode { get; set; }

    /// <summary>站点标签（业务侧备注名）。</summary>
    public string? StationTag { get; set; }

    /// <summary>站点是否启用（false = 禁用，下发任务时不可选）。</summary>
    public bool StaEnable { get; set; }

    /// <summary>站点下的货物区列表（储位可含多个货物区）。</summary>
    public List<string>? CargoAreas { get; set; }

    /// <summary>是否允许车辆掉头。</summary>
    public bool AllowTurn { get; set; }

    /// <summary>是否允许车辆避让。</summary>
    public bool AllowAvoid { get; set; }

    /// <summary>是否允许车辆停靠。</summary>
    public bool AllowStop { get; set; }

    /// <summary>载货支持：0/其他 = 带载+空载均可，1 = 仅带载，2 = 仅空载。</summary>
    public int SupportLoadState { get; set; }

    /// <summary>所属列名（货架列标识，如 A、B）。</summary>
    public string? ColumnName { get; set; }

    /// <summary>储位深度（同一列内的货位序号）。</summary>
    public int Depth { get; set; }

    /// <summary>托盘放货角度（弧度）。</summary>
    public double CargoPalletAngle { get; set; }

    /// <summary>绑定的分拣台站点列表。</summary>
    public List<string>? BindPickStations { get; set; }

    /// <summary>绑定的待命点站点列表。</summary>
    public List<string>? BindStandbyStations { get; set; }

    /// <summary>站点绑定的设备列表（输送线、入库口等）。</summary>
    public List<MapStationDevice>? Devices { get; set; }
}

/// <summary>站点绑定的设备（输送线、入库口等），对应 map.json devices 数组元素。</summary>
public class MapStationDevice
{
    /// <summary>设备 ID。</summary>
    public int Id { get; set; }

    /// <summary>设备名。</summary>
    public string? Name { get; set; }

    /// <summary>设备类型（输送线、入库口等）。</summary>
    public string? DeviceType { get; set; }

    /// <summary>设备模板名（决定设备的准入/执行/完成通知行为）。</summary>
    public string? DeviceTemplateName { get; set; }

    /// <summary>设备所属站点 mark。</summary>
    public string? StationMark { get; set; }

    /// <summary>设备所属场景。</summary>
    public string? StationScene { get; set; }

    /// <summary>准入阶段模板（JsonElement 原样保留：仅展示/透传，不做强类型解析）。</summary>
    public JsonElement? Admittance { get; set; }

    /// <summary>执行阶段模板。</summary>
    public JsonElement? Perform { get; set; }

    /// <summary>完成通知阶段模板。</summary>
    public JsonElement? FinishNotice { get; set; }

    /// <summary>准入阶段参数列表。</summary>
    public List<JsonElement>? AdmittanceArgs { get; set; }

    /// <summary>执行阶段参数列表。</summary>
    public List<JsonElement>? PerformArgs { get; set; }

    /// <summary>完成通知阶段参数列表。</summary>
    public List<JsonElement>? FinishNoticeArgs { get; set; }
}

/// <summary>地图连线，字段对应 map.json 的 path 条目。</summary>
public class MapPath
{
    /// <summary>连线 id（Paths 字典的键）。</summary>
    public string? Id { get; set; }

    /// <summary>连线类型（直线/曲线等，仅展示用）。</summary>
    public string? Type { get; set; }

    /// <summary>起点站点 mark（决定通行方向）。</summary>
    public string? StartName { get; set; }

    /// <summary>终点站点 mark。</summary>
    public string? EndName { get; set; }

    /// <summary>连线所在楼层。</summary>
    public int Floor { get; set; }

    /// <summary>是否虚拟连线（逻辑路径，不占实际道路）。</summary>
    public bool IsVirtual { get; set; }

    /// <summary>是否允许通行（false = 封路，车辆绕行）。</summary>
    public bool RouteEnable { get; set; }
}

/// <summary>
/// 持久化到 localStorage 的精简站点信息（地图信息页整理后保存，供其他页面使用）。
/// 只保留任务下发/信号交互需要的字段，丢弃设备等大对象以减小存储体积；
/// 任务下发、信号交互、任务派发、车辆调度页面共享这一份缓存（grcs_map_stations）。
/// </summary>
public class MapStationLite
{
    /// <summary>站点裸编码（不含 _0/_1 后缀，下发时按类型追加，见 MapStationTypeExtensions）。</summary>
    public string Mark { get; set; } = "";

    /// <summary>站点类型位组合（决定下发的 _0/_1 后缀与分拣台判断）。</summary>
    public int StationType { get; set; }

    /// <summary>地图 X 坐标（车辆调度页绘制站点用）。</summary>
    public double X { get; set; }

    /// <summary>地图 Y 坐标。</summary>
    public double Y { get; set; }

    /// <summary>站点所在楼层。</summary>
    public int Floor { get; set; }

    /// <summary>站点是否启用（禁用站点下发任务时不可选）。</summary>
    public bool StaEnable { get; set; }

    /// <summary>站点下的货物区列表。</summary>
    public List<string> CargoAreas { get; set; } = [];

    /// <summary>载货支持：0/其他 = 带载+空载均可，1 = 仅带载，2 = 仅空载。</summary>
    public int SupportLoadState { get; set; }

    /// <summary>是否允许车辆掉头。</summary>
    public bool AllowTurn { get; set; }

    /// <summary>是否允许车辆避让。</summary>
    public bool AllowAvoid { get; set; }

    /// <summary>是否允许车辆停靠。</summary>
    public bool AllowStop { get; set; }
}

/// <summary>localStorage 中的地图站点缓存结构（键：grcs_map_stations）。</summary>
public class MapStationCache
{
    /// <summary>保存时间（界面展示数据新旧，任务下发前提示地图可能过期）。</summary>
    public string SavedAt { get; set; } = "";

    /// <summary>连线数量（用于校验地图数据是否完整加载）。</summary>
    public int PathsCount { get; set; }

    /// <summary>精简站点列表（各页共享的任务下发依据）。</summary>
    public List<MapStationLite> Stations { get; set; } = [];

    /// <summary>地图信息页的筛选状态（切走再回来时恢复界面）。</summary>
    public MapReaderFilterState? Filter { get; set; }
}

/// <summary>地图信息页筛选状态（随地图缓存一起持久化）。</summary>
public class MapReaderFilterState
{
    /// <summary>类型位筛选（0 = 不过滤，按位与匹配 MapStationTypeBits）。</summary>
    public int TypeFilter { get; set; }

    /// <summary>关键字（匹配站点编码/标签）。</summary>
    public string Search { get; set; } = "";

    /// <summary>启停筛选："" = 全部 / "enabled" = 仅启用 / "disabled" = 仅禁用。</summary>
    public string EnableFilter { get; set; } = "";
}

/// <summary>
/// 站点类型位标志（对照 GRCS StationType 枚举），支持组合，如 5 = 道路+储位。
/// 用法：按位与判断（(type & X) != 0）；下发任务时按类型追加 _0/_1 后缀
/// （见 MapStationTypeExtensions.ToWcsCode）；分拣台判断（PickingStation | PeopleStation）
/// 用于从 FINISHED 阶段事件生成分拣完成卡片。
/// </summary>
public static class MapStationTypeBits
{
    public const int NormalRoad = 0b_0000_0001;        // 1   普通道路（可通行、不设停留）
    public const int HighWay = 0b_0000_0010;           // 2   高速路（优先通行）
    public const int StorageLocation = 0b_0000_0100;   // 4   储位（放货点，下发任务追加 _1 后缀）
    public const int TransferPoint = 0b_0000_1000;     // 8   接驳位（与输送线对接，下发任务追加 _0 后缀）
    public const int Parking = 0b_0001_0000;           // 16  停车位
    public const int Charging = 0b_0010_0000;          // 32  充电点
    public const int PickingStation = 0b_0100_0000;    // 64  分拣点（分拣台，追加 _0 后缀；分拣完成卡片据此筛选）
    public const int PeopleStation = 0b_1000_0000;     // 128 人工拣选台（同分拣台，追加 _0 后缀）
    public const int Elevator = 0b_0001_0000_0000;     // 256 电梯点
    public const int Other = 0b_0010_0000_0000;        // 512 其他

    /// <summary>可筛选/展示的类型位列表（位值 + 中文名）：地图信息页筛选下拉与类型列展示共用。</summary>
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
