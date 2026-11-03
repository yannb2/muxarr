namespace Muxarr.Core.Models;

/// <summary>One file-level tag, such as the TMDB id or release name a tagging script wrote into the MKV.</summary>
public class FileTag
{
    public string Name { get; set; } = "";
    public string Value { get; set; } = "";

    // ffprobe mixes container fields into the same format tags: the MKV segment
    // title, muxing app and date, and the MP4 brand boxes. They are not tags.
    private static readonly HashSet<string> ContainerFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "title", "encoder", "creation_time", "major_brand", "minor_version", "compatible_brands"
    };

    /// <summary>True for a format field ffprobe reports that is not a tag (title, encoder...).</summary>
    public static bool IsContainerField(string name)
    {
        return ContainerFields.Contains(name);
    }

    /// <summary>The same names and values, in any order.</summary>
    public static bool SameSet(IReadOnlyList<FileTag> a, IReadOnlyList<FileTag> b)
    {
        static string Key(FileTag t) => t.Name + "\0" + t.Value;
        return a.Count == b.Count
               && a.Select(Key).OrderBy(k => k, StringComparer.Ordinal)
                   .SequenceEqual(b.Select(Key).OrderBy(k => k, StringComparer.Ordinal));
    }

    /// <summary>The global tags in ffprobe's format tags, in file order.</summary>
    public static List<FileTag> FromFormatTags(IEnumerable<KeyValuePair<string, string>>? tags)
    {
        if (tags == null)
        {
            return [];
        }

        return tags
            .Where(t => !string.IsNullOrWhiteSpace(t.Key) && !ContainerFields.Contains(t.Key))
            .Select(t => new FileTag { Name = t.Key, Value = t.Value ?? "" })
            .ToList();
    }
}
