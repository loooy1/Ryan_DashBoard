namespace GRCS.Dashboard.Modules.WcsSimulator.Models;

/// <summary>
/// 功能模块 = 可复用的动作单元：自定义名称 + API 地址 + 参数（值来源）。
/// 任务模板的【起点】【起点之后】【终点】三个时机可各自关联若干模块：
/// 起点模块在下发前执行、起点之后模块在下发接口返回 success 后立即执行、
/// 终点模块在任务 FINISHED 后执行（POST 该 API 地址）。
/// 同一模块同时绑在多个时机时会在各时机各执行一次（各自上下文）。
/// </summary>
public class WcsModule
{
    /// <summary>唯一标识（自动生成）。</summary>
    public string Id { get; set; } = "";

    /// <summary>自定义名称。</summary>
    public string Name { get; set; } = "";

    /// <summary>API 地址（GRCS 相对路径，如 /api/v1/container_remove；经后端通用转发接口 /api/wcs/forward 代发 GRCS）。</summary>
    public string ApiUrl { get; set; } = "";

    /// <summary>参数列表（参数名 + 取值来源），执行时 POST { 参数名: 值 } JSON。</summary>
    public List<WorkParam> Params { get; set; } = [];
}

/// <summary>
/// 模块注册表：运行时列表 = 用户在信号交互页创建的模块（持久化到 localStorage 键 grcs_si_modules）。
/// </summary>
public static class ModuleRegistry
{
    private static readonly List<WcsModule> Custom = [];

    public static IReadOnlyList<WcsModule> All => Custom;

    /// <summary>载入模块集合（替换当前集合）。</summary>
    public static void SetCustoms(IEnumerable<WcsModule> modules)
    {
        Custom.Clear();
        Custom.AddRange(modules);
    }

    /// <summary>新增模块；Id 冲突时返回 false。</summary>
    public static bool Add(WcsModule m)
    {
        if (Custom.Any(x => string.Equals(x.Id, m.Id, StringComparison.OrdinalIgnoreCase))) return false;
        Custom.Add(m);
        return true;
    }

    /// <summary>按 Id 删除模块。</summary>
    public static bool Remove(string id) => Custom.RemoveAll(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase)) > 0;

    /// <summary>按 Id 查找模块。</summary>
    public static WcsModule? Find(string id) => Custom.FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));
}
