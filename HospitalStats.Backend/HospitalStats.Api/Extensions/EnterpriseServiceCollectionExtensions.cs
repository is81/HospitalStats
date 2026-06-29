using Microsoft.Extensions.DependencyInjection;

namespace HospitalStats.Api.Extensions;

/// <summary>
/// 企业版服务注册扩展。
/// 社区版不调用此方法。企业版 Program.cs 在社区版 DI 容器基础上调用
/// <c>builder.Services.AddEnterpriseServices()</c> 注册企业版实现。
/// </summary>
public static class EnterpriseServiceCollectionExtensions
{
    /// <summary>
    /// 注册企业版服务。参数均为企业版实现类型。
    /// 如果不传入某个实现，对应功能在运行时不可用（返回 null/no-op）。
    /// </summary>
    public static IServiceCollection AddEnterpriseServices(
        this IServiceCollection services,
        Type? reportSchedulerType = null,
        Type? dashboardExtensionType = null,
        Type? dataAnalyzerType = null,
        Type? authProviderType = null,
        Type? licenseValidatorType = null)
    {
        if (reportSchedulerType != null && typeof(IReportScheduler).IsAssignableFrom(reportSchedulerType))
            services.AddSingleton(typeof(IReportScheduler), reportSchedulerType);

        if (dashboardExtensionType != null && typeof(IDashboardExtension).IsAssignableFrom(dashboardExtensionType))
            services.AddSingleton(typeof(IDashboardExtension), dashboardExtensionType);

        if (dataAnalyzerType != null && typeof(IDataAnalyzer).IsAssignableFrom(dataAnalyzerType))
            services.AddScoped(typeof(IDataAnalyzer), dataAnalyzerType);

        if (authProviderType != null && typeof(IAuthProvider).IsAssignableFrom(authProviderType))
            services.AddSingleton(typeof(IAuthProvider), authProviderType);

        if (licenseValidatorType != null && typeof(ILicenseValidator).IsAssignableFrom(licenseValidatorType))
            services.AddSingleton(typeof(ILicenseValidator), licenseValidatorType);

        return services;
    }
}
