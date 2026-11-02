using System.Globalization;

namespace Muxarr.Core.Extensions;

/// <summary>
/// "2160p", "1080p"... from a video track's "WIDTHxHEIGHT". Width counts as
/// much as height, so a scope film cropped to 3840x1600 is still 2160p and
/// 1920x800 is still 1080p.
/// </summary>
public static class ResolutionLabel
{
    public static string From(string? dimensions)
    {
        if (!TryParse(dimensions, out var width, out var height))
        {
            return "";
        }

        return (width, height) switch
        {
            ( >= 7000, _) or (_, >= 4000) => "4320p",
            ( >= 3200, _) or (_, >= 2000) => "2160p",
            ( >= 2200, _) or (_, >= 1300) => "1440p",
            ( >= 1700, _) or (_, >= 950) => "1080p",
            ( >= 1200, _) or (_, >= 680) => "720p",
            (_, >= 540) => "576p",
            _ => "480p"
        };
    }

    private static bool TryParse(string? dimensions, out int width, out int height)
    {
        width = height = 0;
        var parts = dimensions?.Split('x', 'X');
        return parts is { Length: 2 }
               && int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out width)
               && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out height)
               && width > 0 && height > 0;
    }
}
