using Muxarr.Core.Models;

namespace Muxarr.Data.Entities;

/// <summary>
/// The track names a file arrived with, before any profile renamed them. Kept in
/// the database (<see cref="MediaFile.OriginalTrackNames"/>) and in a hidden
/// file next to the media (<see cref="TrackNameSidecar"/>), so the names survive
/// a database reset and a rename by Radarr or Sonarr.
/// </summary>
public class TrackNameBackup
{
    /// <summary>File name (no folder) the entry was last seen under.</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>
    /// Size in bytes when last seen. A rename by an arr keeps it, an upgrade to a
    /// new release does not, so it tells the two apart.
    /// </summary>
    public long Size { get; set; }

    public long DurationMs { get; set; }
    public DateTime FirstSeen { get; set; }
    public DateTime LastSeen { get; set; }
    public List<OriginalTrack> Tracks { get; set; } = [];

    /// <summary>Chapter titles the file arrived with. Empty for entries written before chapters were kept.</summary>
    public List<ChapterInfo> Chapters { get; set; } = [];

    /// <summary>
    /// The file's global tags (TMDB id, release name...), the latest set seen. A file
    /// that comes back with none keeps these, so a remux or retag that lost them can be undone.
    /// </summary>
    public List<FileTag> GlobalTags { get; set; } = [];

    /// <summary>The original title of the chapter starting at <paramref name="startMs"/>, if there was one.</summary>
    public string? OriginalChapterTitle(long startMs)
    {
        return Chapters.FirstOrDefault(c => c.StartsAt(startMs))?.Title;
    }
}

/// <summary>One track as it was in the original file.</summary>
public class OriginalTrack
{
    public int Index { get; set; }
    public MediaTrackType Type { get; set; }
    public string Codec { get; set; } = string.Empty;
    public string LanguageCode { get; set; } = string.Empty;
    public string LanguageName { get; set; } = string.Empty;
    public int AudioChannels { get; set; }
    public bool IsForced { get; set; }
    public bool IsHearingImpaired { get; set; }
    public bool IsCommentary { get; set; }
    public string? Name { get; set; }
}

/// <summary>The hidden <c>.muxarr-tracks.json</c> file, one per folder.</summary>
public class TrackNameSidecar
{
    public int Version { get; set; } = 1;
    public List<TrackNameBackup> Files { get; set; } = [];
}
