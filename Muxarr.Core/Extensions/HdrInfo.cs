using System.Text.RegularExpressions;
using Muxarr.Core.FFmpeg;

namespace Muxarr.Core.Extensions;

/// <summary>
/// HDR and Dolby Vision facts for a video track, read from ffprobe's stream
/// info (and dovi_tool for the profile 7 enhancement layer type).
/// </summary>
public static partial class HdrInfo
{
    public const string Hdr10 = "HDR10";
    public const string Hdr10Plus = "HDR10+";
    public const string Hlg = "HLG";
    public const string Fel = "FEL";
    public const string Mel = "MEL";

    /// <summary>The base layer's HDR format from the transfer characteristics, or null for SDR.</summary>
    public static string? ParseHdrFormat(string? colorTransfer)
    {
        return colorTransfer?.ToLowerInvariant() switch
        {
            "smpte2084" => Hdr10,
            "arib-std-b67" => Hlg,
            _ => null
        };
    }

    /// <summary>True when any probed frame carries HDR10+ (SMPTE ST 2094-40) side data.</summary>
    public static bool HasHdr10Plus(FFprobeFramesResult? frames)
    {
        return frames?.Frames.Any(f => f.SideDataList?.Any(s =>
            s.SideDataType?.Contains(FFprobeFrameSideData.Hdr10PlusMarker, StringComparison.OrdinalIgnoreCase) == true
            || s.SideDataType?.Contains("2094-40", StringComparison.Ordinal) == true) == true) == true;
    }

    public static FFprobeSideData? FindDoviRecord(IEnumerable<FFprobeSideData>? sideData)
    {
        return sideData?.FirstOrDefault(s =>
            string.Equals(s.SideDataType, FFprobeSideData.DoviConfigurationRecord, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// "5", "7", "8.1", "8.4"... from the DOVI configuration record. Profile 8's
    /// suffix is the base layer compatibility: 1 = HDR10, 2 = SDR, 4 = HLG.
    /// </summary>
    public static string? ParseDvProfile(FFprobeSideData? dovi)
    {
        if (dovi?.DvProfile is not { } profile)
        {
            return null;
        }

        return profile == 8 && dovi.DvBlSignalCompatibilityId is int compat && compat > 0
            ? $"8.{compat}"
            : profile.ToString();
    }

    /// <summary>
    /// True when the record says a profile 7 enhancement layer is present,
    /// the only case where FEL vs MEL means something.
    /// </summary>
    public static bool HasEnhancementLayer(FFprobeSideData? dovi)
    {
        return dovi is { DvProfile: 7, ElPresentFlag: 1 };
    }

    [GeneratedRegex(@"Profiles?:\s*7\s*\(([^)]*)\)")]
    private static partial Regex ProfileSevenSummaryRegex();

    /// <summary>
    /// Reads dovi_tool's "info --summary" output: "Profile: 7 (FEL)" -> FEL.
    /// A sample mixing both ("7 (FEL, MEL)") counts as FEL, since the full
    /// enhancement layer is what a player must not drop.
    /// </summary>
    public static string? ParseDoviToolSummary(string? summary)
    {
        if (string.IsNullOrEmpty(summary))
        {
            return null;
        }

        var match = ProfileSevenSummaryRegex().Match(summary);
        if (!match.Success)
        {
            return null;
        }

        var types = match.Groups[1].Value;
        if (types.Contains(Fel, StringComparison.OrdinalIgnoreCase))
        {
            return Fel;
        }

        return types.Contains(Mel, StringComparison.OrdinalIgnoreCase) ? Mel : null;
    }

    /// <summary>"DV P7 FEL", "DV P8.1", "DV P5", or "" when the track has no Dolby Vision.</summary>
    public static string FormatDolbyVision(string? dvProfile, string? dvLayer)
    {
        if (string.IsNullOrEmpty(dvProfile))
        {
            return "";
        }

        return string.IsNullOrEmpty(dvLayer) ? $"DV P{dvProfile}" : $"DV P{dvProfile} {dvLayer}";
    }
}
