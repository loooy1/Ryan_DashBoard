namespace GRCS.Dashboard.Services;

/// <summary>
/// 三层导航状态管理：功能 → 项目 → 页面。
/// 每个浏览器标签页一个实例（Scoped），互不影响。
/// </summary>
public class ModuleNavigationService
{
    /// <summary>当前激活的功能（第一层），如 "wcs"。null 表示在主页。</summary>
    public string? ActiveFeature { get; private set; }

    /// <summary>当前激活的项目（第二层），如 "thailand-twd"。null 表示还没选项目。</summary>
    public string? ActiveProject { get; private set; }

    /// <summary>当前功能下的项目列表（第二层导航项）。</summary>
    public IReadOnlyList<ProjectDef> Projects { get; private set; } = [];

    /// <summary>当前项目下的页面导航（第三层导航项）。</summary>
    public IReadOnlyList<SubNavItem> SubNavItems { get; private set; } = [];

    /// <summary>选中项目后主侧边栏缩回为图标模式（60px），否则展开（260px）。</summary>
    public bool SidebarCollapsed => ActiveProject != null;

    /// <summary>导航状态变化时触发，MainLayout 订阅此事件来刷新 UI。</summary>
    public event Action? StateChanged;

    /// <summary>
    /// 第一层 → 第二层：选中一个功能，显示该项目列表。
    /// </summary>
    /// <param name="featureId">功能标识，如 "wcs"</param>
    /// <param name="projects">该功能下的项目列表</param>
    public void SelectFeature(string featureId, List<ProjectDef> projects)
    {
        ActiveFeature = featureId;
        ActiveProject = null;          // 切功能时清空已选项目
        Projects = projects;
        SubNavItems = [];             // 清空页面导航
        StateChanged?.Invoke();       // 通知 MainLayout 重绘
    }

    /// <summary>
    /// 第二层 → 第三层：选中一个项目，侧边栏缩回，显示页面导航。
    /// </summary>
    /// <param name="projectId">项目标识，如 "thailand-twd"</param>
    /// <param name="subNavItems">该项目下的页面导航列表</param>
    public void SelectProject(string projectId, List<SubNavItem> subNavItems)
    {
        ActiveProject = projectId;
        SubNavItems = subNavItems;
        StateChanged?.Invoke();
    }

    /// <summary>回到主页：清空所有选中状态，侧边栏展开。</summary>
    public void GoHome()
    {
        ActiveFeature = null;
        ActiveProject = null;
        Projects = [];
        SubNavItems = [];
        StateChanged?.Invoke();
    }
}

/// <summary>第二层：一个项目（如"泰国TWD项目"）。</summary>
public record ProjectDef(string Id, string Title, string Icon);

/// <summary>第三层：一个页面导航项（如"任务下发"）。</summary>
public record SubNavItem(string Label, string Href, string Icon);
