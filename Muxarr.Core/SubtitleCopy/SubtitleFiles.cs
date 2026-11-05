using System.Text;
using Muxarr.Core.Language;

namespace Muxarr.Core.SubtitleCopy;

/// <summary>
/// The files the Copy subtitles page can take tracks from: videos (their subtitle
/// tracks) and loose subtitle files, whose language and flags come from the name.
/// </summary>
public static class SubtitleFiles
{
    public static readonly string[] VideoExtensions = [".mkv", ".mk3d", ".mp4", ".m4v"];
    public static readonly string[] SubtitleExtensions = [".srt", ".ass", ".ssa", ".sup"];

    private static readonly string[] TextExtensions = [".srt", ".ass", ".ssa"];

    public static bool IsVideo(string path) => HasExtension(path, VideoExtensions);
    public static bool IsLooseSubtitle(string path) => HasExtension(path, SubtitleExtensions);
    public static bool IsDonor(string path) => IsVideo(path) || IsLooseSubtitle(path);
    public static bool IsText(string path) => HasExtension(path, TextExtensions);

    private static bool HasExtension(string path, string[] extensions) =>
        extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The --sub-charset a loose text subtitle needs: null when mkvmerge can tell by
    /// itself (a byte order mark) or the file is not a text subtitle, UTF-8 when the
    /// bytes are valid UTF-8, else Windows-1252, which is what older French and
    /// English SRTs are written in.
    /// </summary>
    public static string? CharsetFor(string path)
    {
        if (!IsText(path) || !File.Exists(path))
        {
            return null;
        }

        var bytes = File.ReadAllBytes(path);
        return CharsetFor(bytes);
    }

    public static string? CharsetFor(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            return null;
        }

        if (bytes.Length >= 2 && (bytes[0] == 0xFF && bytes[1] == 0xFE || bytes[0] == 0xFE && bytes[1] == 0xFF))
        {
            return null;
        }

        try
        {
            new UTF8Encoding(false, true).GetString(bytes);
            return "UTF-8";
        }
        catch (DecoderFallbackException)
        {
            return "windows-1252";
        }
    }

    /// <summary>The text of a loose text subtitle, read in the charset mkvmerge will be told.</summary>
    public static string ReadText(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var charset = CharsetFor(bytes);
        if (charset == "windows-1252")
        {
            return Latin1Windows(bytes);
        }

        using var reader = new StreamReader(new MemoryStream(bytes), Encoding.UTF8, true);
        return reader.ReadToEnd();
    }

    // .NET has no code page 1252 without the CodePages provider; Latin-1 differs only
    // in 0x80-0x9F, where 1252 has curly quotes, the euro sign and the like.
    private static readonly char[] Windows1252High =
    [
        '€', '\u0081', '‚', 'ƒ', '„', '…', '†', '‡', 'ˆ', '‰', 'Š', '‹', 'Œ', '\u008D', 'Ž', '\u008F',
        '\u0090', '‘', '’', '“', '”', '•', '–', '—', '˜', '™', 'š', '›', 'œ', '\u009D', 'ž', 'Ÿ'
    ];

    private static string Latin1Windows(byte[] bytes)
    {
        var chars = new char[bytes.Length];
        for (var i = 0; i < bytes.Length; i++)
        {
            var b = bytes[i];
            chars[i] = b is >= 0x80 and <= 0x9F ? Windows1252High[b - 0x80] : (char)b;
        }

        return new string(chars);
    }

    /// <summary>What a loose subtitle's name says about it: "Show S01E02.fr.forced.srt".</summary>
    public sealed record NameInfo(string? LanguageCode, bool IsForced, bool IsHearingImpaired);

    private static readonly HashSet<string> ForcedWords = new(StringComparer.OrdinalIgnoreCase)
        { "forced", "force", "forcé", "forces", "forcés", "foreign" };

    private static readonly HashSet<string> SdhWords = new(StringComparer.OrdinalIgnoreCase)
        { "sdh", "cc", "hi", "hoh", "sme" };

    // Words a release name ends with that are not a language, even when the
    // language list would read one into them ("hi" is Hindi, "cc" nothing).
    private static readonly HashSet<string> NotLanguages = new(StringComparer.OrdinalIgnoreCase)
        { "sdh", "cc", "hi", "hoh", "sme", "default", "full", "complete", "1080p", "2160p", "720p", "web", "bluray" };

    /// <summary>
    /// Reads the last few dot-separated words of the name (before the extension):
    /// a language as a code or a name (fr, fre, fra, French, Français, fr-CA),
    /// "forced" and "sdh"/"cc"/"hi". Anything else is ignored.
    /// </summary>
    public static NameInfo ParseName(string path)
    {
        var stem = Path.GetFileNameWithoutExtension(path);
        var words = stem.Split('.', StringSplitOptions.RemoveEmptyEntries);
        string? language = null;
        var forced = false;
        var sdh = false;

        // The first word is the episode's own name; only the tail says what the file is.
        for (var i = words.Length - 1; i >= 1 && i >= words.Length - 4; i--)
        {
            var word = words[i].Trim();
            if (ForcedWords.Contains(word))
            {
                forced = true;
                continue;
            }

            if (SdhWords.Contains(word))
            {
                sdh = true;
                continue;
            }

            if (language == null && !NotLanguages.Contains(word) && word.Length >= 2 && !word.Any(char.IsDigit))
            {
                var found = IsoLanguage.Find(word);
                if (found != IsoLanguage.Unknown && found.WriteCode is { } code)
                {
                    language = code;
                }
            }
        }

        return new NameInfo(language, forced, sdh);
    }
}
