using System.Text.Json.Serialization;

namespace GRCS.Dashboard.Modules.WcsSimulator.Models;

/// <summary>
/// 自动化任务选点范围限制（localStorage 键 grcs_auto_range）。
/// 限制 AutoRunService（轮询储位自动化）与 ContainerTaskService（自动容器任务）的选点范围：
/// 开启后，接驳位/储位/分拣台候选池只从限定范围内抽取，可配合指定区域/楼层/站点类型使用。
/// 默认关闭（不限制，行为与之前一致）。
/// </summary>
public class AutoRangeConfig
{
    /// <summary>是否启用范围限制（false = 不限，全地图候选）。</summary>
    public bool Enabled { get; set; }

    /// <summary>站点类型位过滤（0 = 不限类型；按位与匹配 MapStationTypeBits）。</summary>
    public int TypeFilter { get; set; }

    /// <summary>楼层过滤（0 = 不限楼层）。</summary>
    public int FloorFilter { get; set; }

    /// <summary>手动指定的站点 Mark 白名单（为空 = 不限；非空时仅从这些站点选点）。</summary>
    public List<string> Marks { get; set; } = [];

    /// <summary>
    /// 按范围限制过滤候选站点池。
    /// 未启用时原样返回（行为不变）；启用后依次按 类型位 + 楼层 + Mark 白名单 收窄。
    /// 手动白名单与类型/楼层为 AND 关系（同时满足才保留）。
    /// </summary>
    public List<MapStationLite> ApplyTo(IEnumerable<MapStationLite> stations)
    {
        if (!Enabled) return stations.ToList();

        IEnumerable<MapStationLite> pool = stations;
        if (TypeFilter != 0)
            pool = pool.Where(s => (s.StationType & TypeFilter) != 0);
        if (FloorFilter != 0)
            pool = pool.Where(s => s.Floor == FloorFilter);
        if (Marks.Count > 0)
        {
            var marks = Marks.Where(m => !string.IsNullOrWhiteSpace(m))
                .Select(m => m.Trim())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            pool = pool.Where(s => marks.Contains(s.Mark));
        }
        return pool.ToList();
    }

    /// <summary>解析用户输入文本为 Mark 白名单（支持中文/英文逗号、空格、换行分隔）。</summary>
    public static List<string> ParseMarks(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];
        return text.Split([',', '，', ';', '；', ' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }
}

/// <summary>
/// 站点地图框选器（StationMapPicker）传给 JS 的配置。
/// 数据口径：Stations = 当前范围卡片的 TypeFilter 命中的全部站点（含禁用，禁用置灰不可选）；
/// Floors 取启用站点楼层去重排序；Preselected = 既有 Mark 白名单（打开时预选中，增量编辑）。
/// 白名单自身不参与候选过滤（否则越选越窄），关闭回写时 Mark 与地图大小写不敏感匹配。
/// </summary>
public class StationMapPickerConfig
{
    /// <summary>可选楼层（启用站点楼层去重升序）。</summary>
    public List<int> Floors { get; set; } = [];

    /// <summary>默认楼层（范围卡片楼层=全部时取最低层；否则取范围楼层）。</summary>
    public int InitialFloor { get; set; }

    /// <summary>候选站点（含禁用，JS 端按 StaEnable 置灰不可选）。</summary>
    public List<StationMapPickerStation> Stations { get; set; } = [];

    /// <summary>打开时预选中的既有 Mark 白名单。</summary>
    public List<string> Preselected { get; set; } = [];
}

/// <summary>框选器画布里的单个站点（精简字段，仅供 JS 绘制/命中）。</summary>
public class StationMapPickerStation
{
    public string Mark { get; set; } = "";
    public int StationType { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public int Floor { get; set; }
    public bool StaEnable { get; set; }
}