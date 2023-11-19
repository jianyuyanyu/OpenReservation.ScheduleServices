using System.Collections.Concurrent;
using Hangfire;
using NuGet.Versioning;
using OpenReservation.ScheduleServices.Services;
using ReferenceResolver;
using WeihanLi.Extensions;

namespace OpenReservation.ScheduleServices.Jobs;

public sealed class NuGetPackageWatchJob : AbstractJob
{
    private readonly ConcurrentDictionary<string, NuGetVersion> _versions = new();
    
    public NuGetPackageWatchJob(ILoggerFactory loggerFactory, IServiceProvider serviceProvider) : base(loggerFactory, serviceProvider)
    {
    }

    public override string CronExpression { get; } = Cron.Hourly();

    protected override async Task ExecuteInternalAsync(IServiceProvider scopeServiceProvider, CancellationToken cancellationToken)
    {
        var configuration = scopeServiceProvider.GetRequiredService<IConfiguration>();
        var packageIds = configuration.GetAppSetting("WatchingNugetPackageIds")
            ?.Split(',', StringSplitOptions.RemoveEmptyEntries);
        if (packageIds.IsNullOrEmpty()) return;

        var nugetHelper = scopeServiceProvider.GetRequiredService<INuGetHelper>();
        var notificationService = scopeServiceProvider.GetRequiredService<INotificationService>();
        foreach (var pkgId in packageIds)
        {
            var packageVersion = await nugetHelper.GetLatestPackageVersion(pkgId, false, cancellationToken);
            if (packageVersion is null)
            {
                Logger.LogInformation("No version found for package {PackageId}", pkgId);
                continue;
            }
            
            if (_versions.TryGetValue(pkgId, out var version))
            {
                if (packageVersion != version)
                {
                    _versions[pkgId] = packageVersion;
                    Logger.LogInformation("Package `{PackageId}` latest version changed to {PackageVersion}", 
                        pkgId, packageVersion);
                    // notification
                    await notificationService.SendNotificationAsync(
                        $"[NuGetPackageWatcher]Package {pkgId} version change from {version} to {packageVersion}");
                }
                else
                {
                    Logger.LogInformation("Package `{PackageId}` latest version is still {PackageVersion}", 
                        pkgId, packageVersion);
                }
            }
            else
            {
                _versions[pkgId] = packageVersion;
                Logger.LogInformation("Package `{PackageId}` latest version is {PackageVersion}", 
                    pkgId, packageVersion);
            }
        }
    }
}