using Microsoft.Extensions.Diagnostics.HealthChecks;
using Muxarr.Core.DoviTool;

namespace Muxarr.Web.HealthChecks.Checks;

// Optional tool: without it Dolby Vision profiles are still detected, only
// FEL vs MEL stays unknown, so a missing binary is Degraded, not Unhealthy.
public class DoviToolHealthCheck : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        return await DoviTool.IsAvailable()
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Degraded("dovi_tool is not available; Dolby Vision FEL/MEL will not be detected");
    }
}
