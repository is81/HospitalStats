namespace HospitalStats.Api.Extensions;

/// <summary>
/// 定时报告调度器接口（企业版功能）。
/// 社区版不提供实现，企业版通过 <see cref="CronReportScheduler"/> 实现。
/// </summary>
public interface IReportScheduler
{
    /// <summary>
    /// 添加一个定时任务。
    /// </summary>
    /// <param name="configId">查询配置 ID</param>
    /// <param name="cronExpression">Cron 表达式</param>
    /// <param name="recipients">邮件接收人列表</param>
    /// <param name="outputFormat">输出格式：excel / pdf</param>
    Task ScheduleAsync(int configId, string cronExpression, string[] recipients, string outputFormat);

    /// <summary>
    /// 移除一个定时任务。
    /// </summary>
    Task UnscheduleAsync(int configId);

    /// <summary>
    /// 获取所有已调度任务的状态。
    /// </summary>
    Task<IReadOnlyList<ScheduledReportInfo>> GetScheduledReportsAsync();

    /// <summary>
    /// 手动触发一次报告执行。
    /// </summary>
    Task TriggerAsync(int configId);
}

/// <summary>
/// 已调度报告的信息摘要。
/// </summary>
public class ScheduledReportInfo
{
    public int ConfigId { get; init; }
    public string ConfigName { get; init; } = string.Empty;
    public string CronExpression { get; init; } = string.Empty;
    public string[] Recipients { get; init; } = [];
    public string OutputFormat { get; init; } = "excel";
    public DateTime? LastRunAt { get; init; }
    public bool LastRunSuccess { get; init; }
    public string? LastErrorMessage { get; init; }
}
