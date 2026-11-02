namespace Muxarr.Core.Models;

/// <summary>One chapter as probed, or as a custom conversion wants it named.</summary>
public class ChapterInfo
{
    public long StartMs { get; set; }
    public long EndMs { get; set; }
    public string? Title { get; set; }

    /// <summary>Chapter start times from ffprobe and mkvextract round differently; this is close enough to be the same chapter.</summary>
    public const long StartToleranceMs = 2;

    public bool StartsAt(long ms)
    {
        return Math.Abs(StartMs - ms) <= StartToleranceMs;
    }

    public static string FormatTime(long ms)
    {
        var t = TimeSpan.FromMilliseconds(ms);
        return $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}";
    }
}
