using System.ComponentModel.DataAnnotations;

namespace Muxarr.Core.Extensions;

/// <summary>
/// An object-based audio layer carried on top of a core codec: Dolby Atmos
/// rides on TrueHD or E-AC-3 (JOC), DTS:X on DTS-HD MA. Detected from the
/// stream itself, never from the track name.
/// </summary>
public enum AudioFeature
{
    [Display(Name = "")]
    None,

    [Display(Name = "Atmos")]
    Atmos,

    [Display(Name = "DTS:X")]
    DtsX
}

public static class AudioFeatureExtensions
{
    /// <summary>
    /// Reads the feature from ffprobe's stream profile. ffmpeg 6.1+ decoders set
    /// it while probing: "Dolby TrueHD + Dolby Atmos", "Dolby Digital Plus + Dolby
    /// Atmos", "DTS-HD MA + DTS:X" and "DTS-HD MA + DTS:X IMAX".
    /// </summary>
    public static AudioFeature ParseAudioFeature(string? profile)
    {
        if (string.IsNullOrEmpty(profile))
        {
            return AudioFeature.None;
        }

        if (profile.Contains("Atmos", StringComparison.OrdinalIgnoreCase))
        {
            return AudioFeature.Atmos;
        }

        if (profile.Contains("DTS:X", StringComparison.OrdinalIgnoreCase))
        {
            return AudioFeature.DtsX;
        }

        return AudioFeature.None;
    }
}
