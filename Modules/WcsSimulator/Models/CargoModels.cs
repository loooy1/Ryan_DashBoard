namespace GRCS.Dashboard.Modules.WcsSimulator.Models;

/// <summary>GRCS /api/Cargo 查询响应体（对应 MessageResult&lt;PagedRecords&lt;CargoView&gt;&gt;）。</summary>
public class CargoQueryResult
{
    /// <summary>0 = 正常，非零 = 异常。</summary>
    public int Code { get; set; }

    public string? Message { get; set; }

    public CargoPagedData? Data { get; set; }
}

/// <summary>分页容器数据（对应 PagedRecords&lt;CargoView&gt;）。</summary>
public class CargoPagedData
{
    public int TotalCount { get; set; }

    public List<CargoInventoryItem>? Records { get; set; }
}

/// <summary>单个容器库存条目（对应 GRCS CargoView 的关键字段）。</summary>
public class CargoInventoryItem
{
    /// <summary>容器 ID（数据库主键）。</summary>
    public int Id { get; set; }

    /// <summary>容器编码（= WCS 下发的 ContainerCode）。</summary>
    public string? Code { get; set; }

    /// <summary>老家站点编码。</summary>
    public string? HomeStationMark { get; set; }

    /// <summary>老家场景。</summary>
    public string? HomeStationScene { get; set; }

    /// <summary>老家放货区。</summary>
    public string? HomeCargoAreaName { get; set; }

    /// <summary>当前举升状态：true = 在车上。</summary>
    public bool IsLoaded { get; set; }

    /// <summary>所属机器人。</summary>
    public string? RobotId { get; set; }

    /// <summary>锁定状态。</summary>
    public bool IsLocked { get; set; }

    /// <summary>当前所属点位。</summary>
    public string? CurrentStationCode { get; set; }

    /// <summary>当前站点的货物区名称。</summary>
    public string? CurrentCargoAreaName { get; set; }

    /// <summary>所属订单 id（非空 = 正在执行任务）。</summary>
    public string? CurrentOrderId { get; set; }
}
