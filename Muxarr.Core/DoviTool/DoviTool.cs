using Muxarr.Core.Extensions;
using Muxarr.Core.Utilities;

namespace Muxarr.Core.DoviTool;

/// <summary>
/// Wrapper around quietvoid's dovi_tool, used only to tell a profile 7 FEL
/// (full enhancement layer) from a MEL. ffprobe already gives the profile;
/// the layer type is in the RPU, which only dovi_tool reads.
/// </summary>
public static class DoviTool
{
    internal const string Executable = "dovi_tool";

    // The layer type does not change mid-film, so a short sample is enough and
    // keeps an 80 GB remux to a few seconds of reading.
    private const int SampleFrames = 500;

    /// <summary>
    /// "FEL", "MEL", or null when dovi_tool is missing, fails, or finds no profile 7 RPU.
    /// Matroska only: dovi_tool reads MKV directly but not MP4.
    /// </summary>
    public static async Task<string?> GetEnhancementLayerType(string file, TimeSpan? timeout = null)
    {
        var rpu = Path.Combine(Path.GetTempPath(), $"muxarr-{Guid.NewGuid():N}.rpu");
        try
        {
            var extract = await ProcessExecutor.ExecuteProcessAsync(Executable,
                argumentList: ["extract-rpu", "--limit", SampleFrames.ToString(), "-o", rpu, file],
                timeout: timeout ?? TimeSpan.FromMinutes(5));
            if (!extract.Success || !File.Exists(rpu))
            {
                return null;
            }

            var info = await ProcessExecutor.ExecuteProcessAsync(Executable,
                argumentList: ["info", "--summary", "-i", rpu],
                timeout: TimeSpan.FromMinutes(1));
            return info.Success ? HdrInfo.ParseDoviToolSummary(info.Output) : null;
        }
        catch (Exception)
        {
            // Not installed (Win32Exception), timed out, or unreadable: the
            // profile from ffprobe still stands, only the layer stays unknown.
            return null;
        }
        finally
        {
            try
            {
                File.Delete(rpu);
            }
            catch (IOException)
            {
            }
        }
    }

    public static async Task<bool> IsAvailable()
    {
        try
        {
            var result = await ProcessExecutor.ExecuteProcessAsync(Executable, "--version", TimeSpan.FromSeconds(5));
            return result.ExitCode == 0;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
