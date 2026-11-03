using System.Xml.Linq;
using Muxarr.Core.Models;
using Muxarr.Core.Utilities;

namespace Muxarr.Core.MkvToolNix;

/// <summary>
/// Edits a Matroska file's global tags in place of their old values. The file's
/// tags are extracted to XML and only the global ones are kept; each top-level
/// simple tag is matched by the name ffprobe shows for it, then given its new
/// value or removed. Tags the user added go into the first global tag block.
/// Targets, languages, nested tags and track tags stay as they were.
/// </summary>
public static class MkvGlobalTags
{
    private const string Executable = "mkvextract";

    private static readonly string[] TargetUids = ["TrackUID", "EditionUID", "ChapterUID", "AttachmentUID"];

    public sealed record Result(bool HasTags, string? Error);

    /// <summary>
    /// Writes the file's global tags, edited to <paramref name="wanted"/>, to <paramref name="xmlPath"/>.
    /// HasTags is false when none are left, which the tools write as "remove all global tags".
    /// </summary>
    public static async Task<Result> WriteEdited(string mkvPath, IReadOnlyList<FileTag> wanted, string xmlPath)
    {
        var extract = await ProcessExecutor.ExecuteProcessAsync(Executable,
            $"\"{mkvPath}\" tags \"{xmlPath}\"", TimeSpan.FromMinutes(2));
        if (extract.ExitCode != 0)
        {
            var detail = string.IsNullOrWhiteSpace(extract.Error) ? extract.Output : extract.Error;
            return new Result(false, $"mkvextract failed: {detail}");
        }

        XDocument source;
        try
        {
            // A file without tags gives no output at all.
            source = File.Exists(xmlPath) && new FileInfo(xmlPath).Length > 0
                ? MkvChapters.Load(xmlPath)
                : new XDocument(new XElement("Tags"));
        }
        catch (Exception e)
        {
            return new Result(false, $"the extracted tags could not be read: {e.Message}");
        }

        var edited = Edit(source, wanted);
        var hasTags = edited.Root!.Elements("Tag").Any();
        if (hasTags)
        {
            edited.Save(xmlPath);
        }

        return new Result(hasTags, null);
    }

    /// <summary>The global tag blocks of <paramref name="source"/>, edited to hold <paramref name="wanted"/>.</summary>
    public static XDocument Edit(XDocument source, IReadOnlyList<FileTag> wanted)
    {
        var root = new XElement("Tags");
        var remaining = wanted.ToList();

        foreach (var tag in source.Root?.Elements("Tag").Where(IsGlobal) ?? [])
        {
            var copy = new XElement(tag);
            foreach (var simple in copy.Elements("Simple").ToList())
            {
                var key = KeyOf(simple);
                if (key == null || FileTag.IsContainerField(key))
                {
                    // Not one the page lists, so not one the user could have changed.
                    continue;
                }

                var match = remaining.FirstOrDefault(w => w.Name == key);
                if (match == null)
                {
                    simple.Remove();
                    continue;
                }

                remaining.Remove(match);
                SetValue(simple, match.Value);
            }

            if (copy.Elements("Simple").Any())
            {
                root.Add(copy);
            }
        }

        if (remaining.Count > 0)
        {
            var target = root.Elements("Tag").FirstOrDefault();
            if (target == null)
            {
                target = new XElement("Tag", new XElement("Targets", new XElement("TargetTypeValue", "50")));
                root.Add(target);
            }

            foreach (var tag in remaining)
            {
                target.Add(new XElement("Simple", new XElement("Name", tag.Name), new XElement("String", tag.Value)));
            }
        }

        return new XDocument(new XDeclaration("1.0", "UTF-8", null), root);
    }

    private static bool IsGlobal(XElement tag)
    {
        var targets = tag.Element("Targets");
        return targets == null || !TargetUids.Any(uid => targets.Elements(uid).Any(e => e.Value.Trim() != "0"));
    }

    // ffprobe names a tag in a language other than "und" NAME-lang.
    private static string? KeyOf(XElement simple)
    {
        var name = simple.Element("Name")?.Value;
        if (string.IsNullOrEmpty(name))
        {
            return null;
        }

        var language = simple.Element("TagLanguage")?.Value.Trim();
        return string.IsNullOrEmpty(language) || language == "und" ? name : $"{name}-{language}";
    }

    private static void SetValue(XElement simple, string value)
    {
        simple.Element("Binary")?.Remove();
        var text = simple.Element("String");
        if (text == null)
        {
            text = new XElement("String");
            simple.Element("Name")!.AddAfterSelf(text);
        }

        text.Value = value;
    }
}
