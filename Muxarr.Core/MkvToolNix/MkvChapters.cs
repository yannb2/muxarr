using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using Muxarr.Core.Models;
using Muxarr.Core.Utilities;

namespace Muxarr.Core.MkvToolNix;

/// <summary>
/// Renames Matroska chapters without touching anything else about them. The
/// file's own chapters are extracted to XML, only the titles of the chapters
/// that match a wanted start time are changed, and the result is handed back
/// to mkvpropedit or mkvmerge. Editions, nesting, hidden flags and other
/// languages stay as they were.
/// </summary>
public static class MkvChapters
{
    private const string Executable = "mkvextract";

    public sealed record Result(int Renamed, string? Error);

    /// <summary>Extracts the chapters of <paramref name="mkvPath"/>, renames them and writes <paramref name="xmlPath"/>.</summary>
    public static async Task<Result> WriteRenamed(string mkvPath, IReadOnlyList<ChapterInfo> chapters, string xmlPath)
    {
        var extract = await ProcessExecutor.ExecuteProcessAsync(Executable,
            $"\"{mkvPath}\" chapters \"{xmlPath}\"", TimeSpan.FromMinutes(2));
        if (extract.ExitCode != 0)
        {
            var detail = string.IsNullOrWhiteSpace(extract.Error) ? extract.Output : extract.Error;
            return new Result(0, $"mkvextract failed: {detail}");
        }

        if (!File.Exists(xmlPath) || new FileInfo(xmlPath).Length == 0)
        {
            return new Result(0, "the file has no chapters to rename");
        }

        XDocument doc;
        try
        {
            doc = Load(xmlPath);
        }
        catch (Exception e)
        {
            return new Result(0, $"the extracted chapters could not be read: {e.Message}");
        }

        var matched = Rename(doc, chapters);
        if (matched == 0)
        {
            return new Result(0, "none of the file's chapters start where the renamed ones do");
        }

        doc.Save(xmlPath);
        return new Result(matched, null);
    }

    // mkvextract writes a DOCTYPE; the default reader refuses DTDs outright.
    public static XDocument Load(string xmlPath)
    {
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore };
        using var reader = XmlReader.Create(xmlPath, settings);
        return XDocument.Load(reader);
    }

    /// <summary>
    /// Sets the first display string of every chapter atom whose start matches
    /// a wanted chapter with a title. Returns how many atoms matched.
    /// </summary>
    public static int Rename(XDocument doc, IReadOnlyList<ChapterInfo> chapters)
    {
        var matched = 0;
        foreach (var atom in doc.Descendants("ChapterAtom"))
        {
            if (ParseTime(atom.Element("ChapterTimeStart")?.Value) is not { } start)
            {
                continue;
            }

            var wanted = chapters.FirstOrDefault(c => !string.IsNullOrEmpty(c.Title) && c.StartsAt(start));
            if (wanted == null)
            {
                continue;
            }

            var display = atom.Element("ChapterDisplay");
            if (display == null)
            {
                display = new XElement("ChapterDisplay",
                    new XElement("ChapterString", ""),
                    new XElement("ChapterLanguage", "und"));
                atom.Add(display);
            }

            var text = display.Element("ChapterString");
            if (text == null)
            {
                text = new XElement("ChapterString");
                display.AddFirst(text);
            }

            text.Value = wanted.Title!;
            matched++;
        }

        return matched;
    }

    /// <summary>"00:01:23.456789000" to whole milliseconds.</summary>
    public static long? ParseTime(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var parts = value.Trim().Split(':');
        if (parts.Length != 3
            || !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var hours)
            || !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var minutes)
            || !decimal.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
        {
            return null;
        }

        return (long)Math.Round((hours * 3600 + minutes * 60 + seconds) * 1000m);
    }
}
