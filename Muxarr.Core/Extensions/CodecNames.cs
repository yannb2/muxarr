using Muxarr.Core.Models;

namespace Muxarr.Core.Extensions;

/// <summary>
/// Resolves the words the {codec} track name placeholder writes. Every codec
/// has a detected label: its built-in display name ("TrueHD", "AC-3"), with the
/// object audio layer added when the stream carries one ("TrueHD Atmos",
/// "E-AC-3 Atmos", "DTS:X"). A profile can map any label to its own words.
/// </summary>
public static class CodecNames
{
    /// <summary>
    /// The detected label for a track, before any custom name is applied.
    /// </summary>
    public static string Label(IMediaTrack track)
    {
        var baseName = track.Codec.FormatCodec();
        return track.AudioFeature switch
        {
            AudioFeature.Atmos => $"{baseName} Atmos",
            AudioFeature.DtsX => AudioFeature.DtsX.DisplayName(),
            _ => baseName
        };
    }

    /// <summary>
    /// The name {codec} writes. Order: a custom name for the full label
    /// ("TrueHD Atmos"), then a custom name for the core codec plus the
    /// feature ("Dolby TrueHD" + " Atmos"), then the label itself.
    /// </summary>
    public static string Resolve(IMediaTrack track, IReadOnlyDictionary<string, string>? customNames)
    {
        var label = Label(track);
        if (customNames == null || customNames.Count == 0)
        {
            return label;
        }

        if (TryGet(customNames, label, out var custom))
        {
            return custom;
        }

        if (track.AudioFeature == AudioFeature.Atmos
            && TryGet(customNames, track.Codec.FormatCodec(), out var coreName))
        {
            return $"{coreName} {AudioFeature.Atmos.DisplayName()}";
        }

        return label;
    }

    /// <summary>
    /// Every label a profile can rename for one track type, in display order.
    /// Object audio labels follow the codec they ride on.
    /// </summary>
    public static IReadOnlyList<string> Labels(MediaTrackType type)
    {
        return type switch
        {
            MediaTrackType.Audio => AudioCodecExtensions.SelectableValues
                .SelectMany(AudioLabels)
                .ToList(),
            MediaTrackType.Video => Enum.GetValues<VideoCodec>()
                .Where(c => c != VideoCodec.Unknown)
                .Select(c => c.DisplayName())
                .Concat(VideoFeatureLabels)
                .ToList(),
            MediaTrackType.Subtitles => SubtitleCodecExtensions.SelectableValues
                .Select(c => c.DisplayName())
                .ToList(),
            _ => []
        };
    }

    /// <summary>
    /// HDR formats and Dolby Vision layers, renamable like codecs: {hdr} and
    /// {dvlayer} (also inside the Dolby Vision wording) write the custom name.
    /// </summary>
    public static readonly IReadOnlyList<string> VideoFeatureLabels =
        [HdrInfo.Hdr10, HdrInfo.Hdr10Plus, HdrInfo.Hlg, HdrInfo.Fel, HdrInfo.Mel];

    /// <summary>A custom name for a single label, or the label itself ("" for null).</summary>
    public static string Rename(string? label, IReadOnlyDictionary<string, string>? customNames)
    {
        if (string.IsNullOrEmpty(label))
        {
            return "";
        }

        return customNames != null && TryGet(customNames, label, out var custom) ? custom : label;
    }

    private static IEnumerable<string> AudioLabels(AudioCodec codec)
    {
        var name = codec.DisplayName();
        yield return name;

        switch (codec)
        {
            case AudioCodec.TrueHd or AudioCodec.Eac3:
                yield return $"{name} {AudioFeature.Atmos.DisplayName()}";
                break;
            case AudioCodec.DtsHdMa:
                yield return AudioFeature.DtsX.DisplayName();
                break;
        }
    }

    // Profiles round-trip through JSON, which drops a dictionary's comparer,
    // so match keys without regard to case here.
    private static bool TryGet(IReadOnlyDictionary<string, string> names, string key, out string value)
    {
        foreach (var (k, v) in names)
        {
            if (string.Equals(k, key, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(v))
            {
                value = v.Trim();
                return true;
            }
        }

        value = string.Empty;
        return false;
    }
}
