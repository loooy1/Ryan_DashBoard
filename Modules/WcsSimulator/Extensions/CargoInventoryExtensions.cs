using GRCS.Dashboard.Modules.WcsSimulator.Models;

namespace GRCS.Dashboard.Modules.WcsSimulator.Extensions;

/// <summary>
/// 容器库存条目（CargoInventoryItem）的扩展方法：托盘/货物识别。
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
}
