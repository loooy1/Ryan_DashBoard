using GRCS.Dashboard.Modules.WcsSimulator.Models;

namespace GRCS.Dashboard.Modules.WcsSimulator.Extensions;

/// <summary>容器库存条目（CargoInventoryItem）的扩展方法：托盘/货物识别与带货判断。</summary>
public static class CargoInventoryExtensions
{
    /// <summary>是否托盘（编码含 Container 字样）。</summary>
    public static bool IsPallet(this CargoInventoryItem c)
        => c.Code?.Contains("Container", StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>是否货物（编码含 Cargo 字样）。</summary>
    public static bool IsCargo(this CargoInventoryItem c)
        => c.Code?.Contains("Cargo", StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>托盘是否带货：同库位存在货物即视为带货（与自动化任务的空托/带货托区分一致）。</summary>
    public static bool IsLoadedPallet(this CargoInventoryItem pallet, IEnumerable<CargoInventoryItem> records)
    {
        if (pallet.IsCargo() || string.IsNullOrEmpty(pallet.CurrentStationCode)) return false;
        return records.Any(r => r.IsCargo() && string.Equals(r.CurrentStationCode, pallet.CurrentStationCode, StringComparison.OrdinalIgnoreCase));
    }
}
