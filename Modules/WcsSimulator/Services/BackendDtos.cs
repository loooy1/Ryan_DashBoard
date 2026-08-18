using System.Text.Json;

namespace GRCS.Dashboard.Modules.WcsSimulator.Services;

/// <summary>GrcsBackend /api/wcs/auto/status 快照（与后端 AutoStatusDto 同构）。</summary>
public class AutoStatusSnapshot
{
    public bool Running { get; set; }
    public string AutoTabId { get; set; } = "";
    public int Interval { get; set; }
    public int FlowMode { get; set; }
    public int Dispatched { get; set; }
    public string Status { get; set; } = "";
    public InventoryCountsDto AutoInventory { get; set; } = new();
    public bool ContainerBusy { get; set; }
    public string BatchTabId { get; set; } = "";
    public int ContainerDone { get; set; }
    public int ContainerTotal { get; set; }
    public string ContainerStatus { get; set; } = "";
    public InventoryCountsDto ContainerInventory { get; set; } = new();
    public bool MoveRunning { get; set; }
    public string MoveTabId { get; set; } = "";
    public bool DispatchActive { get; set; }
    public WcsSettingsDto Settings { get; set; } = new();
    public SignalFlagsDto Signals { get; set; } = new();
}

public class InventoryCountsDto
{
    public int EmptyPallets { get; set; }
    public int LoadedPallets { get; set; }
    public int Cargos { get; set; }
    public int PairedCargos { get; set; }
}

public class WcsSettingsDto
{
    public string GrcsBaseUrl { get; set; } = "http://localhost:8224";
    public string SceneName { get; set; } = "";
}

public class SignalFlagsDto
{
    public bool AdmittanceAuto { get; set; }
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
    public string Time = "";
    public string Message = "";
    public string Color = "#94a3b8";
}

/// <summary>后端日志条目（Id 自增，sinceId 增量拉取）。</summary>
public class BackendLogEntry
{
    public long Id { get; set; }
    public string Time { get; set; } = "";
    public string Message { get; set; } = "";
    public string Color { get; set; } = "#94a3b8";
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

public class LogsResponse
{
    public long MaxId { get; set; }
    public List<BackendLogEntry> Entries { get; set; } = [];
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

/// <summary>信号确认抢占响应（POST /api/wcs/signal-confirm/{kind}/{taskId}）。</summary>
public class ClaimResponse
{
    public bool Claimed { get; set; }
}

/// <summary>分拣已发送的编辑参数（workflow_state sent 行的 value JSON）。</summary>
public class SortingSendParams
{
    public string ReturnTaskId { get; set; } = "";
    public bool RemoveContainer { get; set; }
    public string DestStation { get; set; } = "";
    public string DestArea { get; set; } = "";
}
