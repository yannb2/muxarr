using System.Text.RegularExpressions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Muxarr.Core.Utilities;

namespace Muxarr.Web.HealthChecks.Checks;

public class FFmpegHealthCheck(ILogger<FFmpegHealthCheck> logger) : IHealthCheck
{
    // 8.1+ required for the MP4 udta.name round-trip.
    private const int MinMajor = 8;
    private const int MinMinor = 1;

    private static readonly Regex VersionRegex =
        new(@"^ffmpeg version n?(\d+)\.(\d+)", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await ProcessExecutor.ExecuteProcessAsync("ffmpeg", "-version", TimeSpan.FromSeconds(5));

            if (result.ExitCode != 0)
            {
                return HealthCheckResult.Unhealthy($"ffmpeg exited with code {result.ExitCode}");
            }

            var firstLine = (result.Output ?? string.Empty).Split('\n', 2)[0].Trim();
            var match = VersionRegex.Match(firstLine);
            if (!match.Success)
            {
                return HealthCheckResult.Unhealthy($"Could not parse ffmpeg version from: {firstLine}");
            }

            var major = int.Parse(match.Groups[1].Value);
            var minor = int.Parse(match.Groups[2].Value);

            if (major < MinMajor || (major == MinMajor && minor < MinMinor))
            {
                return HealthCheckResult.Unhealthy(
                    $"ffmpeg {major}.{minor} is too old; Remuxarr requires >= {MinMajor}.{MinMinor}");
            }

            return HealthCheckResult.Healthy($"ffmpeg {major}.{minor}");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Health check failed for ffmpeg");
            return HealthCheckResult.Unhealthy("ffmpeg is not available", ex);
        }
    }
}
