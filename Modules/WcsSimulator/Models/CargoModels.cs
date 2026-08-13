namespace GRCS.Dashboard.Modules.WcsSimulator.Models;

/// <summary>
/// GRCS /api/Cargo 查询响应体（对应 MessageResult&lt;PagedRecords&lt;CargoView&gt;&gt;）。
/// 数据流：库存查询页/自动任务服务 GET /api/Cargo（支持 code/scene/locked 过滤 + 分页），
/// 反序列化为本类型后用于展示与空托/带货托筛选。
/// </summary>
public class CargoQueryResult
{
    /// <summary>0 = 正常，非零 = 异常（如库存服务不可用）。</summary>
    public int Code { get; set; }

    /// <summary>错误/提示信息（Code != 0 时返回）。</summary>
    public string? Message { get; set; }

    /// <summary>分页数据（Code == 0 时有效）。</summary>
    public CargoPagedData? Data { get; set; }
}

/// <summary>分页容器数据（对应 PagedRecords&lt;CargoView&gt;）。</summary>
public class CargoPagedData
{
    /// <summary>符合过滤条件的总记录数（分页计算用，与当前页返回条数无关）。</summary>
    public int TotalCount { get; set; }

    /// <summary>当前页的容器库存条目列表。</summary>
    public List<CargoInventoryItem>? Records { get; set; }
}

/// <summary>单个容器库存条目（对应 GRCS CargoView 的关键字段，托盘与货物共用此结构）。</summary>
public class CargoInventoryItem
{
    /// <summary>容器 ID（GRCS 数据库主键，删除接口 DELETE /api/Cargo/{id} 用它定位）。</summary>
    public int Id { get; set; }

    /// <summary>
    /// 容器编码（= WCS 下发的 ContainerCode）。
    /// 命名约定：Container* = 托盘，Cargo* = 货物——GRCS 没有独立的类型字段，
    /// 托盘/货物识别全靠此前缀（见 CargoInventoryExtensions.IsPallet/IsCargo）。
    /// </summary>
    public string? Code { get; set; }

    /// <summary>老家站点编码（容器任务结束后的默认回库点位）。</summary>
    public string? HomeStationMark { get; set; }

    /// <summary>老家场景。</summary>
    public string? HomeStationScene { get; set; }

    /// <summary>老家放货区。</summary>
    public string? HomeCargoAreaName { get; set; }

    /// <summary>当前举升状态：true = 在车上。</summary>
    public bool IsLoaded { get; set; }

    /// <summary>所属机器人编码（空 = 当前不在任何车上）。</summary>
    public string? RobotId { get; set; }

    /// <summary>锁定状态（true = 业务锁定，锁定容器一般不参与任务）。</summary>
    public bool IsLocked { get; set; }

    /// <summary>当前所属点位（托盘/货物是否同库位、带货判断都靠它比对）。</summary>
    public string? CurrentStationCode { get; set; }

    /// <summary>当前站点的货物区名称（一个储位可含多个货物区，具体放货区由此区分）。</summary>
    public string? CurrentCargoAreaName { get; set; }

    /// <summary>所属订单 id（非空 = 正在执行任务）。</summary>
    public string? CurrentOrderId { get; set; }
}
