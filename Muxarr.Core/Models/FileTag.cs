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
