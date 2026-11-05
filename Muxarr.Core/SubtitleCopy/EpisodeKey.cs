using System.Text.RegularExpressions;

namespace Muxarr.Core.SubtitleCopy;

/// <summary>
/// Which episode a file holds, read from its name: S01E02 (and S01E02E03 or
/// S01E02-E03 for a double episode), 1x02, or an absolute number for anime
/// ("Show - 012 - Title"). Season 0 is the specials.
/// </summary>
public sealed partial record EpisodeKey(int? Season, IReadOnlyList<int> Episodes, int? Absolute)
{
    public bool HasSeasonEpisode => Season != null && Episodes.Count > 0;

    /// <summary>"S01E02", "S01E02-E03", "#012", or "?" when nothing was read.</summary>
    public string Label
    {
        get
        {
            if (HasSeasonEpisode)
            {
                var label = $"S{Season:D2}E{Episodes[0]:D2}";
                return Episodes.Count > 1 ? $"{label}-E{Episodes[^1]:D2}" : label;
            }

            return Absolute is { } a ? $"#{a:D3}" : "?";
        }
    }

    public static EpisodeKey? Parse(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        int? absolute = null;
        var abs = AbsoluteRegex().Match(name);
        if (abs.Success)
        {
            absolute = int.Parse(abs.Groups["abs"].Value);
        }

        var se = SeasonEpisodeRegex().Match(name);
        if (se.Success)
        {
            var episodes = new List<int> { int.Parse(se.Groups["ep"].Value) };
            foreach (Capture more in se.Groups["more"].Captures)
            {
                episodes.Add(int.Parse(more.Value));
            }

            // S01E02-E04 is three episodes; S01E02E03 lists each one.
            if (se.Groups["range"].Success)
            {
                var last = int.Parse(se.Groups["range"].Value);
                for (var e = episodes[^1] + 1; e <= last; e++)
                {
                    episodes.Add(e);
                }
            }

            return new EpisodeKey(int.Parse(se.Groups["season"].Value), episodes, absolute);
        }

        var x = CrossRegex().Match(name);
        if (x.Success)
        {
            return new EpisodeKey(int.Parse(x.Groups["season"].Value), [int.Parse(x.Groups["ep"].Value)], absolute);
        }

        return absolute != null ? new EpisodeKey(null, [], absolute) : null;
    }

    /// <summary>
    /// Same episode: same season and episodes when both names have them, else the
    /// same absolute number when both have one.
    /// </summary>
    public bool SameEpisode(EpisodeKey other)
    {
        if (HasSeasonEpisode && other.HasSeasonEpisode)
        {
            return Season == other.Season && Episodes.SequenceEqual(other.Episodes);
        }

        return Absolute != null && Absolute == other.Absolute;
    }

    [GeneratedRegex(@"(?<![a-z0-9])s(?<season>\d{1,3})[ ._-]?e(?<ep>\d{1,4})(?:[ ._]?e(?<more>\d{1,4}))*(?:-e?(?<range>\d{1,4})(?!\d))?", RegexOptions.IgnoreCase)]
    private static partial Regex SeasonEpisodeRegex();

    [GeneratedRegex(@"(?<![a-z0-9])(?<season>\d{1,2})x(?<ep>\d{2,3})(?!\d)", RegexOptions.IgnoreCase)]
    private static partial Regex CrossRegex();

    // Sonarr's anime names put the absolute number between dashes, sometimes after the
    // season and episode ("Show - S01E12 - 012 - Title"); fansub names after one dash
    // ("[Group] Show - 012 [1080p]").
    [GeneratedRegex(@"(?:^|\s)-\s(?<abs>\d{2,4})(?:v\d)?(?=\s|$|\.)", RegexOptions.IgnoreCase)]
    private static partial Regex AbsoluteRegex();
}
