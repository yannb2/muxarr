using Muxarr.Core.Extensions;

namespace Muxarr.Core.Models;

// Desired output state, or delta of changes. Sibling of MediaSnapshot
// (observed state), not a subclass - fields are nullable with inherit-semantics.
// Serialized as JSON on MediaConversion.ConversionPlan.
public class ConversionPlan : IMedia<TrackPlan>
{
    public List<TrackPlan> Tracks { get; set; } = [];

    // null = inherit source. false = strip. true = preserve.
    public bool? HasChapters { get; set; }
    public bool? HasAttachments { get; set; }

    // Only meaningful for MP4-family containers; Matroska tools ignore it.
    public bool? Faststart { get; set; }

    // Container title (MKV segment title / MP4 title atom).
    // null = inherit source, "" = clear. Mirrors TrackPlan.Name semantics.
    public string? Title { get; set; }

    // Cut anything running past the video. A request, not a timestamp: both
    // writers get their cut at execution time, and it only rides an existing remux.
    public bool TrimToVideoLength { get; set; }

    // Chapter titles to write, matched to the source's chapters by start time.
    // null = leave chapters alone. Matroska only; ignored elsewhere.
    public List<ChapterInfo>? Chapters { get; set; }

    // The global tags the file should end up with: values changed, some removed,
    // some added. null = leave them alone. Matroska only; ignored elsewhere.
    public List<FileTag>? GlobalTags { get; set; }

    // Set while a conversion runs: the global tag XML the Matroska tools read.
    // "" = the file keeps no global tags. Never stored.
    [System.Text.Json.Serialization.JsonIgnore]
    public string? TagsFile { get; set; }

    // Set while a conversion runs: the chapter XML the Matroska tools read,
    // built from the source's own chapters with the new titles. Never stored.
    [System.Text.Json.Serialization.JsonIgnore]
    public string? ChaptersFile { get; set; }

    // Set while a conversion runs: SRT files made from SDH picture subtitles,
    // muxed in as extra tracks. Never stored.
    [System.Text.Json.Serialization.JsonIgnore]
    public List<AddedSubtitle>? AddedSubtitles { get; set; }

    // The tracks the output should have, in order: the planned source tracks
    // with each added subtitle placed before the track it names. An added
    // track has Index -n, n being its place in AddedSubtitles counted from 1,
    // which is also the number of the file mkvmerge reads it from.
    public List<TrackPlan> OutputTracks()
    {
        var added = AddedSubtitles ?? [];
        if (added.Count == 0)
        {
            return Tracks;
        }

        var placed = new HashSet<int>();
        var result = new List<TrackPlan>();
        foreach (var track in Tracks)
        {
            for (var n = 0; n < added.Count; n++)
            {
                if (added[n].BeforeIndex == track.Index && placed.Add(n))
                {
                    result.Add(added[n].ToTrackPlan(n + 1));
                }
            }

            result.Add(track);
        }

        for (var n = 0; n < added.Count; n++)
        {
            if (placed.Add(n))
            {
                result.Add(added[n].ToTrackPlan(n + 1));
            }
        }

        return result;
    }

    bool IMedia<TrackPlan>.HasChapters => HasChapters ?? false;
    bool IMedia<TrackPlan>.HasAttachments => HasAttachments ?? false;
}

// A subtitle file muxed into the output next to the source's tracks.
// BeforeIndex: the source track it goes in front of; -1 puts it last.
public sealed record AddedSubtitle(string Path, string LanguageCode, string Name, int BeforeIndex)
{
    public TrackPlan ToTrackPlan(int fileNumber) => new()
    {
        Index = -fileNumber,
        Type = MediaTrackType.Subtitles,
        Name = Name,
        LanguageCode = LanguageCode,
        IsDefault = false,
        IsForced = false,
        IsHearingImpaired = false,
        IsVisualImpaired = false,
        IsCommentary = false
    };
}

// Desired state for a single track, or a delta of changes from source.
// null  = no opinion / inherit source
// value = desired value
// Name "" = explicit clear (distinct from null)
// NameLocked = planner must not rewrite (user-authored or profile override)
public class TrackPlan : IMediaTrack
{
    public int Index { get; set; }
    public MediaTrackType Type { get; set; }

    public string? Name { get; set; }
    public string? LanguageCode { get; set; }
    public bool? IsDefault { get; set; }
    public bool? IsForced { get; set; }
    public bool? IsHearingImpaired { get; set; }
    public bool? IsVisualImpaired { get; set; }
    public bool? IsCommentary { get; set; }
    public bool? IsOriginal { get; set; }
    public bool? IsDub { get; set; }

    public bool NameLocked { get; set; }

    // Explicit IMediaTrack impls — coerce nullable plan values to the non-null
    // observed-track shape. Plans carry no codec/channel/duration info.
    string IMediaTrack.LanguageCode => LanguageCode ?? string.Empty;
    string IMediaTrack.LanguageName => string.Empty;
    string IMediaTrack.Codec => string.Empty;
    int IMediaTrack.AudioChannels => 0;
    AudioFeature IMediaTrack.AudioFeature => AudioFeature.None;
    string? IMediaTrack.HdrFormat => null;
    string? IMediaTrack.DvProfile => null;
    string? IMediaTrack.DvLayer => null;
    string? IMediaTrack.Resolution => null;
    long IMediaTrack.DurationMs => 0;
    bool IMediaTrack.IsDefault => IsDefault ?? false;
    bool IMediaTrack.IsForced => IsForced ?? false;
    bool IMediaTrack.IsCommentary => IsCommentary ?? false;
    bool IMediaTrack.IsHearingImpaired => IsHearingImpaired ?? false;
    bool IMediaTrack.IsVisualImpaired => IsVisualImpaired ?? false;
    bool IMediaTrack.IsOriginal => IsOriginal ?? false;
    bool IMediaTrack.IsDub => IsDub ?? false;
}
