namespace HospitalStats.Api.Extensions;

/// <summary>
/// 数据分析扩展接口（企业版功能）。
/// 提供 DRG/DIP 医保分析等专项数据分析能力。
/// 社区版不提供实现。
/// </summary>
public interface IDataAnalyzer
{
    /// <summary>
    /// 执行 DRG/DIP 分组分析。
    /// </summary>
    /// <param name="parameters">分析参数（时间范围、科室范围等）</param>
    /// <returns>DRG/DIP 分析结果。</returns>
    Task<DrgAnalysisResult> AnalyzeDrgAsync(DrgAnalysisParameters parameters);

    /// <summary>
    /// 获取支持的 DRG 版本列表。
    /// </summary>
    Task<IReadOnlyList<string>> GetSupportedDrgVersionsAsync();
}

/// <summary>
/// DRG/DIP 分析请求参数。
/// </summary>
public class DrgAnalysisParameters
{
    public DateTime StartDate { get; init; }
    public DateTime EndDate { get; init; }
    public string? DeptCode { get; init; }
    public string DrgVersion { get; init; } = "CHS-DRG 1.2";
}

/// <summary>
/// DRG/DIP 分析结果。
/// </summary>
public class DrgAnalysisResult
{
    public string DrgVersion { get; init; } = string.Empty;
    public int TotalCases { get; init; }
    public decimal Cmi { get; init; }
    public decimal AvgRw { get; init; }
    public int LowVarianceCases { get; init; }
    public int HighVarianceCases { get; init; }
    public List<DrgGroupResult> Groups { get; init; } = new();
}

/// <summary>
/// 单个 DRG 分组的分析结果。
/// </summary>
public class DrgGroupResult
{
    public string DrgCode { get; init; } = string.Empty;
    public string DrgName { get; init; } = string.Empty;
    public int CaseCount { get; init; }
    public decimal AvgCost { get; init; }
    public decimal AvgPayment { get; init; }
    public decimal Pnl { get; init; }
    public decimal Weight { get; init; }
}
