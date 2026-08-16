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