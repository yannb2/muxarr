using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Muxarr.Core.SubtitleCopy;

/// <summary>
/// Matches a movie in the library ("Corpse Bride (2005)") with donor files named the
/// scene way ("Corpse.Bride.2005.1080p.BluRay.REMUX-GRP.mkv", "Corpse Bride 2005.fr.srt").
/// </summary>
public static partial class MovieName
{
    /// <summary>The title words and year of a Radarr folder or file name: "Corpse Bride (2005) {edition-X}".</summary>
    public static (string Title, int? Year) Parse(string name)
    {
        var withoutEdition = EditionRegex().Replace(name, " ");
        var year = YearRegex().Match(withoutEdition);
        var title = year.Success ? withoutEdition[..year.Index] : withoutEdition;
        return (Normalize(title), year.Success ? int.Parse(year.Groups["year"].Value) : null);
    }

    /// <summary>
    /// The donor's name (or its folder's, for a release folder holding "movie.mkv") starts
    /// with the movie's title words, and has its year when the library name has one.
    /// </summary>
    public static bool Matches(string movieFolderName, string donorPath)
    {
        var (title, year) = Parse(movieFolderName);
        if (title.Length == 0)
        {
            return false;
        }

        var names = new[]
        {
            Path.GetFileNameWithoutExtension(donorPath),
            Path.GetFileName(Path.GetDirectoryName(donorPath) ?? "")
        };

        foreach (var name in names.Where(n => n.Length > 0))
        {
            var words = Normalize(name);
            if (!(words + " ").StartsWith(title + " ", StringComparison.Ordinal))
            {
                continue;
            }

            if (year == null || Regex.IsMatch(words, $@"(^| ){year}( |$)"))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Lower case, no accents, "&amp;" as "and", everything else that is not a letter or digit a single space.</summary>
    public static string Normalize(string text)
    {
        var decomposed = text.Replace("&", " and ").Replace("'", "").Replace("’", "").Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            builder.Append(char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : ' ');
        }

        return SpacesRegex().Replace(builder.ToString(), " ").Trim();
    }

    [GeneratedRegex(@"\{edition-[^}]*\}", RegexOptions.IgnoreCase)]
    private static partial Regex EditionRegex();

    // The first year in brackets or between dots/spaces, after at least one title character.
    [GeneratedRegex(@"(?<=.)[\s.(\[_-]+(?<year>(19|20)\d{2})(?=[\s.)\]_-]|$)")]
    private static partial Regex YearRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex SpacesRegex();
}
