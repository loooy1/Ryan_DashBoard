namespace GRCS.Dashboard.Modules.WcsSimulator.Models;

/// <summary>
/// 工作参数的取值来源：固定值，或从任务里取对应字段（起点值/终点值/任务容器等）。
/// </summary>
public enum WorkValueSource
{
    /// <summary>固定值：取 <see cref="WorkParam.FixedValue"/>。</summary>
    Fixed,

    /// <summary>起点值：任务起点站点编码（StationCode[0]）。</summary>
    StartPoint,

    /// <summary>终点值：任务终点站点编码（StationCode[末位]）。</summary>
    EndPoint,

    /// <summary>任务容器：台账/下发时填写的容器号。</summary>
    TaskContainer,

    /// <summary>任务仓库：下发场景名。</summary>
    TaskWarehouse,

    /// <summary>任务类型：TaskType 协议值。</summary>
    TaskType,

    /// <summary>任务号：TaskId。</summary>
    TaskId,

    /// <summary>当前时间：生成/下发时的当前时刻（用于 MsgTime 等）。</summary>
    Now,
}

/// <summary>
/// 一项模块参数 = 「参数名 + 值来源」。模块执行时按值来源解析实际取值：
/// 固定值取 <see cref="FixedValue"/>，否则取任务对应字段（如起点值 → 任务起点站点）。
/// </summary>
public class WorkParam
{
    /// <summary>参数名（推荐：StationCode / ContainerCode / Warehouse 等，可自定义）。</summary>
    public string Name { get; set; } = "";

    /// <summary>取值来源。</summary>
    public WorkValueSource Source { get; set; } = WorkValueSource.Fixed;

    /// <summary>Source == Fixed 时的固定值。</summary>
    public string FixedValue { get; set; } = "";
}

/// <summary>
/// 任务模板中的一个点（起点 / 终点）。
/// 每个点可关联若干功能模块（ModuleRunService 后端执行），并带站点类型约束。
/// </summary>
public class TaskPoint
{
    /// <summary>点的显示名（起点/终点标签），也是下发表单该行输入框的标签。</summary>
    public string Label { get; set; } = "";

    /// <summary>此点「之前」绑定的功能模块 Id 列表（起点之前 = 下发任务组之前执行；终点无此前置阶段）。</summary>
    public List<string> BeforeModules { get; set; } = [];

    /// <summary>此点「之后」绑定的功能模块 Id 列表（起点之后 = 下发返回 success 后执行；终点之后 = 任务 FINISHED 后执行）。</summary>
    public List<string> AfterModules { get; set; } = [];

    /// <summary>站点类型位约束（MapStationTypeBits 组合）：该点站点须满足 (StationType & Bits) != 0。</summary>
    public int StationTypeBits { get; set; }
}

/// <summary>
/// 预设任务类型模板：一条模板 = 一种任务类型的全部行为。
/// 仅两类点：起点 <see cref="Start"/> 与终点 <see cref="End"/>。
/// 起点含两段模块：起点之前（下发前）/ 起点之后（CREATED 后）；终点只有终点之后（FINISHED 后）。
/// 每个点带站点类型约束。
/// 本模板是【纯数据】；下发表单、校验、模块执行都由页面/后端按模板通用处理。
/// </summary>
public record TaskTypeTemplate(
    string Value,                          // RawOrderType 枚举值（task_receive 的 TaskType）
    string Label,                          // 中文显示名（芯片标题）
    string Description,                    // 简短描述（芯片副标题）
    string Category,                       // 分类：in/out/sort/move/other（预留）
    TaskPoint Start,                       // 起点（起点之前 + 起点之后模块）
    TaskPoint End                          // 终点（终点之后模块）
)
{
    /// <summary>是否需要容器号：勾选后编辑任务多显示一行容器号；默认需要。</summary>
    public bool NeedsContainer { get; set; } = true;

    /// <summary>容器号前缀（随机生成时使用；留空默认 Container）。</summary>
    public string ContainerPrefix { get; set; } = "";

    /// <summary>是否随机生成容器号：勾选后编辑任务的容器号自动生成、不可手填。</summary>
    public bool RandomContainer { get; set; }
}

/// <summary>
/// 任务类型模板注册表：内置模板已清空（本轮改版后任务类型一律由界面创建），
/// 运行时列表 = 用户创建的自定义模板（持久化到后端 task_templates + localStorage 兜底）。
/// </summary>
public static class TaskTypeRegistry
{
    /// <summary>内置模板（当前为空；恢复历史内置类型时在此追加）。</summary>
    private static readonly TaskTypeTemplate[] Builtins = [];

    private static readonly List<TaskTypeTemplate> Custom = [];
    private static List<TaskTypeTemplate>? _all;

    /// <summary>全部模板（内置 + 自定义），顺序即「选择任务类型」芯片展示顺序。</summary>
    public static IReadOnlyList<TaskTypeTemplate> All
    {
        get
        {
            if (_all == null)
            {
                _all = [.. Builtins, .. Custom];
            }
            return _all;
        }
    }

    /// <summary>仅用户自定义模板（用于持久化）。</summary>
    public static IReadOnlyList<TaskTypeTemplate> Customs => Custom;

    /// <summary>启动时载入自定义模板（替换当前自定义集，不影响内置）。</summary>
    public static void SetCustoms(IEnumerable<TaskTypeTemplate> customs)
    {
        Custom.Clear();
        Custom.AddRange(customs);
        _all = null;
    }

    /// <summary>新增模板；Value 与已有模板冲突时返回 false。</summary>
    public static bool Add(TaskTypeTemplate t)
    {
        if (All.Any(x => string.Equals(x.Value, t.Value, StringComparison.OrdinalIgnoreCase))) return false;
        Custom.Add(t);
        _all = null;
        return true;
    }

    /// <summary>按 Value 删除自定义模板；内置模板不可删，返回是否删除成功。</summary>
    public static bool Remove(string value)
    {
        var n = Custom.RemoveAll(x => string.Equals(x.Value, value, StringComparison.OrdinalIgnoreCase));
        if (n > 0) _all = null;
        return n > 0;
    }

    /// <summary>是否为用户自定义模板（内置返回 false，用于芯片上是否显示删除按钮）。</summary>
    public static bool IsCustom(string value) => Custom.Any(x => string.Equals(x.Value, value, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// 按旧 Value 原地替换自定义模板（保留其在列表中的位置，不改变排序）；
    /// 内置模板不可替换，返回是否替换成功。
    /// </summary>
    public static bool Replace(string oldValue, TaskTypeTemplate replacement)
    {
        var idx = Custom.FindIndex(x => string.Equals(x.Value, oldValue, StringComparison.OrdinalIgnoreCase));
        if (idx < 0) return false;
        Custom[idx] = replacement;
        _all = null;
        return true;
    }
}
