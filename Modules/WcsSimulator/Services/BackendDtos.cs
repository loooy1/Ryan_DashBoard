namespace GRCS.Dashboard.Modules.WcsSimulator.Services;

/// <summary>GrcsBackend /api/wcs/auto/status 快照（与后端 AutoTemplateStatusDto 同构）。</summary>
public class AutoStatusSnapshot
{
    public bool Running { get; set; }
    public string AutoTabId { get; set; } = "";
    public int Interval { get; set; }                // 毫秒
    public string ActiveTemplateId { get; set; } = "";
    public string ActiveTemplateName { get; set; } = "";
    public int Executed { get; set; }
    public string Status { get; set; } = "";
    public bool MoveRunning { get; set; }
    public string MoveTabId { get; set; } = "";
    public List<AutoTemplateDto> Templates { get; set; } = [];
    public WcsSettingsDto Settings { get; set; } = new();
    public SignalFlagsDto Signals { get; set; } = new();
}

/// <summary>自动化模板（与后端 AutoTemplateDto 同构）。</summary>
public class AutoTemplateDto
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public List<AutoStepDto> Steps { get; set; } = [];
}

/// <summary>自动化模板步骤（与后端 AutoStepDto 同构）。</summary>
public class AutoStepDto
{
    public string Kind { get; set; } = "PickPallet";   // PickPallet | PickLoadedPallet | PickCargo | RunTemplate
    public string PalletFilter { get; set; } = "Empty"; // Empty | Loaded | Any
    public string TemplateValue { get; set; } = "";    // 任务模板 Value（RunTemplate 用）
    public string Label { get; set; } = "";
    /// <summary>容器是否使用前置步骤挑选的托盘/货物号：true（默认）取前置；false 按模板自动生成。</summary>
    public bool UsePickedContainer { get; set; } = true;
    /// <summary>起点是否取自前置步骤的终点：true（默认）取上一步终点（选托盘时即托盘所在站）；false 按模板起点类型在范围内自行选点。</summary>
    public bool UsePickedStart { get; set; } = true;
    /// <summary>等待完成再继续下一步：含终点模块时强制等待模块 success；无终点模块时按 FINISHED 等待。默认开启，可单步关闭。</summary>
    public bool WaitForFinish { get; set; } = true;
}

public class WcsSettingsDto
{
    public string GrcsBaseUrl { get; set; } = "http://localhost:8224";
    public string SceneName { get; set; } = "";
}

public class SignalFlagsDto
{
    public bool ArrivalAuto { get; set; }
    public bool RemovalAuto { get; set; }
    public bool AutoSend { get; set; }
}

/// <summary>移动任务循环租约登记结果（POST /api/wcs/auto/move/start）。</summary>
public class MoveLeaseResult
{
    public bool Success { get; set; }
    public string? Reason { get; set; }
}

/// <summary>单条移动任务下发结果（POST /api/wcs/auto/move/dispatch → GRCS /api/RawOrder/ChangeFloor）。</summary>
public class MoveDispatchResult
{
    public bool Success { get; set; }
    public int Code { get; set; }
    public string Json { get; set; } = "";
}

/// <summary>自动化日志条目（后端日志流映射为前端展示格式）。</summary>
public class AutoLogEntry
{
    public string Time { get; set; } = "";
    public string Message { get; set; } = "";
    public string Color { get; set; } = "#94a3b8";
}

/// <summary>日志轮次（后端按轮次分组：每轮一个标题 + 该轮所有条目）。</summary>
public class LogRoundDto
{
    public string RoundId { get; set; } = "";
    public string ParentRoundId { get; set; } = "";
    public string Title { get; set; } = "";
    public string StartTime { get; set; } = "";
    public bool Completed { get; set; }
    public List<AutoLogEntry> Entries { get; set; } = [];
}

/// <summary>库存分类汇总（纯空托 / 带货托 / 纯货物 / 锁定中）。</summary>
public class InventorySummaryDto
{
    public int Empty { get; set; }
    public int Loaded { get; set; }
    public int Cargo { get; set; }
    public int Locked { get; set; }
}

/// <summary>准入状态（GET /api/wcs/status：自动模式 + 待确认数）。</summary>
public class AdmittanceStatusDto
{
    public bool AutoMode { get; set; }
    public int PendingCount { get; set; }
}

/// <summary>WCS 代理响应（/api/wcs/grcs/* 统一返回 { ok, code, json }）。</summary>
public class GrcsProxyResult
{
    public bool Ok { get; set; }
    public int Code { get; set; }
    public string Json { get; set; } = "";
}

/// <summary>选点范围配置（与后端 RangeConfigDto 同构）。</summary>
public class RangeConfigDto
{
    public bool Enabled { get; set; }
    public int TypeFilter { get; set; }
    public int FloorFilter { get; set; }
    public List<string> Marks { get; set; } = [];
}

/// <summary>地图缓存响应（GET /api/wcs/map）。</summary>
public class MapCacheDto
{
    public string SavedAt { get; set; } = "";
    public int PathsCount { get; set; }
    public List<GRCS.Dashboard.Modules.WcsSimulator.Models.MapStationLite> Stations { get; set; } = [];
}

/// <summary>地图上传负载（POST /api/wcs/map/upload，与后端 MapUploadDto 同构）。</summary>
public class MapUploadPayload
{
    public string SavedAt { get; set; } = "";
    public int PathsCount { get; set; }
    public List<GRCS.Dashboard.Modules.WcsSimulator.Models.MapStationLite> Stations { get; set; } = [];
}

/// <summary>信号确认状态行（GET /api/wcs/signal-confirm，workflow_state 表行）。</summary>
public class WorkflowStateRowDto
{
    public string Kind { get; set; } = "";
    public string TaskId { get; set; } = "";
    public string? Value { get; set; }
    public string Time { get; set; } = "";
}

/// <summary>分拣已发送的编辑参数（workflow_state sent 行的 value JSON）。</summary>
public class SortingSendParams
{
    public string ReturnTaskId { get; set; } = "";
    public bool RemoveContainer { get; set; }
    public string DestStation { get; set; } = "";
    public string DestArea { get; set; } = "";
}

/// <summary>模块执行记录条目（与后端 ModuleExecLogStore.ModuleExecLogEntry 同构；GET /api/wcs/modules/logs 返回）。</summary>
public class ModuleExecLogEntry
{
    public long Id { get; set; }
    public string Time { get; set; } = "";
    public string TaskId { get; set; } = "";
    public string Point { get; set; } = "";    // 起点 / 起点之后 / 终点
    public string Module { get; set; } = "";
    public bool Ok { get; set; }
    public int HttpCode { get; set; }
    public string Detail { get; set; } = "";
}

/// <summary>模块执行记录增量响应（GET /api/wcs/modules/logs）。</summary>
public class ModuleExecLogsResponse
{
    public long MaxId { get; set; }
    public List<ModuleExecLogEntry> Entries { get; set; } = [];
}
