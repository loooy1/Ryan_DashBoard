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
    public int MoveTotal { get; set; }
    public int MoveOk { get; set; }
    public int MoveFail { get; set; }
    public string MoveLastError { get; set; } = "";
    public bool NestRunning { get; set; }
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
    /// <summary>容器是否使用前置步骤挑选的托盘/货物号：true（默认）取前置；false 按模板自动生成。
    /// 旧模板字段（PickedStepIndex 未设置时生效）；新版请用 PickedStepIndex 精确指定引用步骤。</summary>
    public bool UsePickedContainer { get; set; } = true;
    /// <summary>容器来源：0=自动生成；-1=最近前置挑选（旧逻辑）；&gt;0=引用前置第 N 步挑选的容器号。</summary>
    public int PickedStepIndex { get; set; } = 0;
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

/// <summary>纯移动任务循环状态（后端 SignalR「MoveTaskStats」广播 + GET status 轮询字段）。</summary>
public class MoveTaskStatsDto
{
    public bool Running { get; set; }
    public string TabId { get; set; } = "";
    public int Interval { get; set; }
    public int Seq { get; set; }
    public int Total { get; set; }
    public int Ok { get; set; }
    public int Fail { get; set; }
    public string LastError { get; set; } = "";
    public string LastStation { get; set; } = "";
}

/// <summary>归巢模式配置（地图框选巢区站点 Mark 列表，与选点范围 auto_range 相互独立）。</summary>
public class NestConfigDto
{
    public List<string> Marks { get; set; } = [];
}

/// <summary>归巢模式状态（后端 SignalR「NestStats」广播 + GET nest/status）。</summary>
public class NestStatsDto
{
    public bool Running { get; set; }
    public string? LastRunAt { get; set; }
    /// <summary>本次锁定的车队（只调度这些车，与 ReadyVehicles 全量就绪车区分）。</summary>
    public List<string> PoolVehicles { get; set; } = [];
    public int Ok { get; set; }
    public int Fail { get; set; }
    public string? LastError { get; set; }
    public int TargetTotal { get; set; }
    public int TargetOccupied { get; set; }
    public int TargetAssigned { get; set; }
}

/// <summary>归巢执行结果（POST /api/wcs/auto/nest/run）。</summary>
public class NestRunResult
{
    public bool Success { get; set; }
    public string? Reason { get; set; }
}

/// <summary>GRCS 车辆信息（GET /api/wcs/auto/vehicles，归巢车辆多选用）。</summary>
public class VehicleInfoDto
{
    public string Name { get; set; } = "";
    public string ExecutionState { get; set; } = "";
    public string UtilizationState { get; set; } = "";
    public string CurrentTransportOrder { get; set; } = "";
    public bool IsOnline { get; set; }
    public double Power { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }
    public string Location { get; set; } = "";

    /// <summary>归巢就绪判定：在线 + 空闲 + 自动调度 + 无当前任务。</summary>
    public bool IsReady => IsOnline
        && string.Equals(ExecutionState, "IDLE", StringComparison.OrdinalIgnoreCase)
        && string.Equals(UtilizationState, "AUTOMATIC", StringComparison.OrdinalIgnoreCase)
        && string.IsNullOrWhiteSpace(CurrentTransportOrder);
}

/// <summary>异常记录（AGV/软件异常台账，纯 HTTP 读写）。</summary>
public class ExceptionRecordDto
{
    public long Id { get; set; }
    /// <summary>发生时间（yyyy-MM-dd HH:mm:ss）。</summary>
    public string HappenedAt { get; set; } = "";
    /// <summary>车号（可空，记录是哪台车出的问题）。</summary>
    public string? VehicleCode { get; set; }
    /// <summary>现象。</summary>
    public string Phenomenon { get; set; } = "";
    /// <summary>原因。</summary>
    public string Reason { get; set; } = "";
    /// <summary>处理进度（可空，自由文本）。</summary>
    public string? Progress { get; set; }
    /// <summary>责任部门（必填，RCS / WCS / Quicktron）。</summary>
    public string ResponsibleDept { get; set; } = "";
    /// <summary>状态：resolved=已解决 / pending=未回复 / in_progress=进行中 / observing=修复待观察。</summary>
    public string Status { get; set; } = "pending";
    /// <summary>所属项目（空串=未分类，按项目隔离数据）。</summary>
    public string Project { get; set; } = "";
    /// <summary>最近复现时间（可空）。</summary>
    public string? ReproducedAt { get; set; }
    /// <summary>复现次数。</summary>
    public int ReproduceCount { get; set; }
}

/// <summary>项目记录（每日项目日程，纯 HTTP 读写）。</summary>
public class ProjectLogDto
{
    public long Id { get; set; }
    /// <summary>所属日期（yyyy-MM-dd）。</summary>
    public string LogDate { get; set; } = "";
    /// <summary>日程内容。</summary>
    public string Content { get; set; } = "";
    /// <summary>状态：pending=待办 / done=已完成 / cancelled=搁置。</summary>
    public string Status { get; set; } = "pending";
    /// <summary>所属项目（空串=未分类，按项目隔离数据）。</summary>
    public string Project { get; set; } = "";
    /// <summary>备注（可空）。</summary>
    public string? Remark { get; set; }
    public string CreatedAt { get; set; } = "";
    public string UpdatedAt { get; set; } = "";
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

/// <summary>库存明细条目（仅列出有货/托的储位，不包含空储位）。</summary>
public class InventoryDetailItem
{
    public string Code { get; set; } = "";
    public string? Station { get; set; }
    public string? CargoCode { get; set; }
}

/// <summary>库存分类汇总 + 明细（纯空托 / 带货托 / 纯货物 / 锁定中=移动单元数）。</summary>
public class InventorySummaryDto
{
    public int Empty { get; set; }
    public int Loaded { get; set; }
    public int Cargo { get; set; }
    public int Locked { get; set; }
    public List<InventoryDetailItem> EmptyItems { get; set; } = [];
    public List<InventoryDetailItem> LoadedItems { get; set; } = [];
    public List<InventoryDetailItem> CargoItems { get; set; } = [];
    public List<InventoryDetailItem> LockedItems { get; set; } = [];
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

/// <summary>GRCS 接口说明条目（GET /api/wcs/grcs-api-docs，后端 GrcsHttpClient 硬编码接口清单）。</summary>
public class GrcsApiDocDto
{
    public string Name { get; set; } = "";
    public string Method { get; set; } = "";
    public string UrlTemplate { get; set; } = "";
    public string Description { get; set; } = "";
    public string UsedBy { get; set; } = "";
    public List<GrcsApiParamDto> Params { get; set; } = [];
    public string? BodyExample { get; set; }
}

/// <summary>GRCS 接口单个参数说明。</summary>
public class GrcsApiParamDto
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public bool Required { get; set; }
    public string Description { get; set; } = "";
}
