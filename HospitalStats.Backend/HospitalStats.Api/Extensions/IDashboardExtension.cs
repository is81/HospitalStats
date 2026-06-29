namespace HospitalStats.Api.Extensions;

/// <summary>
/// 仪表盘扩展接口（企业版功能）。
/// 提供图表下钻、跨图表联动等高级交互能力。
/// 社区版不提供实现。
/// </summary>
public interface IDashboardExtension
{
    /// <summary>
    /// 根据前端传入的点击数据点，生成下钻查询的筛选条件。
    /// </summary>
    /// <param name="cardId">被点击的卡片 ID</param>
    /// <param name="dataPoint">图表数据点（轴名、值等）</param>
    /// <returns>下钻目标查询配置 ID 及预填充的筛选条件。</returns>
    Task<DrillDownResult?> ResolveDrillDownAsync(int cardId, ChartDataPoint dataPoint);

    /// <summary>
    /// 获取卡片可用的下钻维度列表（供前端渲染右键菜单）。
    /// </summary>
    Task<IReadOnlyList<DrillDimension>> GetDrillDimensionsAsync(int cardId);
}

/// <summary>
/// 图表中的一个数据点（点击事件携带的信息）。
/// </summary>
public class ChartDataPoint
{
    public string SeriesName { get; init; } = string.Empty;
    public string AxisValue { get; init; } = string.Empty;
    public double Value { get; init; }
    public Dictionary<string, string> Extra { get; init; } = new();
}

/// <summary>
/// 下钻结果——指向一个查询配置，附带预填筛选值。
/// </summary>
public class DrillDownResult
{
    public int TargetConfigId { get; init; }
    public Dictionary<string, string> PrefillFilters { get; init; } = new();
    public string Label { get; init; } = string.Empty;
}

/// <summary>
/// 可用的下钻维度。
/// </summary>
public class DrillDimension
{
    public string Key { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
}
