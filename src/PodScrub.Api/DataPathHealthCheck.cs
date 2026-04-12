using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using PodScrub.Application;
using PodScrub.Domain;

namespace PodScrub.Api;

public class DataPathHealthCheck(IOptions<PodScrubOptions> options, IFileSystem fileSystem) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var dataPath = options.Value.DataPath;

        try
        {
            fileSystem.CreateDirectory(dataPath);
            fileSystem.WriteAllText(Path.Combine(dataPath, ".healthcheck"), "ok");
            return Task.FromResult(HealthCheckResult.Healthy($"DataPath '{dataPath}' is writable."));
        }
        catch (Exception ex)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy($"DataPath '{dataPath}' is not writable.", ex));
        }
    }
}
