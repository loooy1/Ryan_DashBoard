using GRCS.Dashboard.Modules.WcsSimulator.Models;

namespace GRCS.Dashboard.Modules.WcsSimulator.Extensions;

/// <summary>
/// 容器库存条目（CargoInventoryItem）的扩展方法：托盘/货物识别与带货判断。
/// GRCS 库存没有独立的容器类型字段，托盘/货物只能靠编码前缀约定区分
/// （Container* = 托盘，Cargo* = 货物），这些方法统一封装该约定。
/// </summary>
public static class CargoInventoryExtensions
{
    /// <summary>是否托盘（编码含 Container 字样）。</summary>
    public static bool IsPallet(this CargoInventoryItem c)
        => c.Code?.Contains("Container", StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>是否货物（编码含 Cargo 字样）。</summary>
    public static bool IsCargo(this CargoInventoryItem c)
        => c.Code?.Contains("Cargo", StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>
    /// 托盘是否带货：同库位存在货物即视为带货（与自动化任务的空托/带货托区分一致）。
    /// 用于任务下发前从库存中筛出可搬运的空托；只有"确定在库位上的托盘"才参与判断，
    /// 不在任何点位的托盘（CurrentStationCode 为空）视为不可用。
    /// </summary>
    public static bool IsLoadedPallet(this CargoInventoryItem pallet, IEnumerable<CargoInventoryItem> records)
    {
        if (pallet.IsCargo() || string.IsNullOrEmpty(pallet.CurrentStationCode)) return false;
        return records.Any(r => r.IsCargo() && string.Equals(r.CurrentStationCode, pallet.CurrentStationCode, StringComparison.OrdinalIgnoreCase));
    }
}
