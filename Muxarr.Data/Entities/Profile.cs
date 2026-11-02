using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Muxarr.Core.Extensions;
using Muxarr.Core.Language;
using Muxarr.Core.Models;
using Muxarr.Data.Extensions;

namespace Muxarr.Data.Entities;

public enum TrackReorderStrategy
{
    /// <summary>Preserve original track order from the source file.</summary>
    DontReorder,

    /// <summary>Reorder tracks alphabetically by language name. Source order is kept within a language.</summary>
    Alphabetical,

    /// <summary>
    /// Reorder tracks to match the AllowedLanguages priority list. Source order is kept within a language.
    /// Requires AllowedLanguages to be configured.
    /// </summary>
    MatchLanguagePriority,

    /// <summary>
    /// Sort by the ordered <see cref="TrackSettings.SortKeys"/>, e.g. forced first, then language
    /// priority, then codec order. Source order breaks the remaining ties.
    /// </summary>
    CustomRules
}

/// <summary>One step of the <see cref="TrackReorderStrategy.CustomRules"/> sort, applied in list order.</summary>
public enum TrackSortKey
{
    [Display(Name = "Forced first")]
    ForcedFirst,

    [Display(Name = "Language priority")]
    LanguagePriority,

    [Display(Name = "Codec order")]
    CodecOrder,

    [Display(Name = "SDH last")]
    HearingImpairedLast,

    [Display(Name = "Commentary last")]
    CommentaryLast,

    [Display(Name = "Best quality first")]
    BestQualityFirst
}

public enum DefaultTrackStrategy
{
    /// <summary>
    /// Preserve original default flags from the source file. No changes made.
    /// </summary>
    DontChange,

    /// <summary>
    /// Commentary, accessibility, and dub tracks are marked as non-default.
    /// All other tracks remain eligible - the player picks based on its own language preferences.
    /// </summary>
    SpecCompliant,

    /// <summary>
    /// Only the first priority language's tracks are marked as default.
    /// Requires AllowedLanguages to be configured for a meaningful priority order.
    /// Use when the player doesn't have language preference settings.
    /// </summary>
    ForceFirstLanguage,

    /// <summary>
    /// Subtitles only: clears the default flag on every track that is not forced, and leaves
    /// forced tracks as they are. A full subtitle never turns itself on.
    /// </summary>
    ForcedOnly,

    /// <summary>
    /// Subtitles only: as <see cref="ForcedOnly"/>, except when the default audio is not in the
    /// first subtitle language and no forced track in that language exists. Then the best full
    /// subtitle in that language becomes the default, so a foreign-language film plays with
    /// subtitles on every player.
    /// </summary>
    ForcedOrForeignAudio
}

public enum TrackFlag
{
    [Display(Name = "SDH")]
    HearingImpaired,

    [Display(Name = "Forced")]
    Forced,

    [Display(Name = "Commentary")]
    Commentary,

    [Display(Name = "AD")]
    VisualImpaired,

    [Display(Name = "Dub")]
    Dub
}

public static class TrackFlagExtensions
{
    public static readonly TrackFlag[] All = Enum.GetValues<TrackFlag>();

    public static bool Matches(this TrackFlag flag, IMediaTrack track)
    {
        return flag switch
        {
            TrackFlag.HearingImpaired => track.IsHearingImpaired,
            TrackFlag.Forced => track.IsForced,
            TrackFlag.Commentary => track.IsCommentary,
            TrackFlag.VisualImpaired => track.IsVisualImpaired,
            TrackFlag.Dub => track.IsDub,
            _ => false
        };
    }
}

public class Profile : AuditableEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public List<string> Directories { get; set; } = new();
    public bool ClearVideoTrackNames { get; set; }
    public bool ClearFileTitle { get; set; }
    public bool RemoveChapters { get; set; }
    public bool TrimToVideoLength { get; set; }
    public bool SkipHardlinkedFiles { get; set; }

    /// <summary>
    /// Keep the audio and subtitle names each file arrived with, in a hidden
    /// <c>.muxarr-tracks.json</c> next to it and in the database.
    /// </summary>
    public bool BackupTrackNames { get; set; }
    public TrackSettings AudioSettings { get; set; } = new();
    public TrackSettings SubtitleSettings { get; set; } = new();

    /// <summary>
    /// Video track naming. Only <see cref="TrackSettings.StandardizeTrackNames"/>,
    /// <see cref="TrackSettings.TrackNameTemplate"/>, <see cref="TrackSettings.CodecNames"/>
    /// and <see cref="TrackSettings.DolbyVisionTemplate"/> apply; a standardized name wins over <see cref="ClearVideoTrackNames"/>.
    /// </summary>
    public TrackSettings VideoSettings { get; set; } = new();
    public ICollection<MediaFile> MediaFiles { get; set; } = new List<MediaFile>();
}

public class TrackSettings
{
    public bool Enabled { get; set; }
    public List<LanguagePreference> AllowedLanguages { get; set; } = new();
    public bool RemoveCommentary { get; set; }
    public bool RemoveImpaired { get; set; }
    public bool AssumeUndeterminedIsOriginal { get; set; }

    /// <summary>
    /// Controls how the default track flag is assigned. Works independently of track removal.
    /// ForceFirstLanguage requires AllowedLanguages to be configured for a meaningful priority order.
    /// </summary>
    public DefaultTrackStrategy DefaultStrategy { get; set; }

    /// <summary>
    /// Controls whether and how tracks are physically reordered in the output file.
    /// </summary>
    public TrackReorderStrategy ReorderStrategy { get; set; }

    public bool StandardizeTrackNames { get; set; }
    public string TrackNameTemplate { get; set; } = string.Empty;
    public Dictionary<TrackFlag, string> TrackNameOverrides { get; set; } = new();

    /// <summary>
    /// Words the {codec} placeholder writes, keyed by the detected codec label
    /// ("TrueHD", "TrueHD Atmos", "AC-3"). A missing or empty entry keeps the
    /// built-in name. Resolved by <see cref="Muxarr.Core.Extensions.CodecNames"/>.
    /// </summary>
    public Dictionary<string, string> CodecNames { get; set; } = new();

    /// <summary>
    /// Video only: what {dv} writes, with {dvprofile} and {dvlayer}. Empty uses
    /// <see cref="Muxarr.Core.Extensions.HdrInfo.DefaultDolbyVisionTemplate"/>
    /// ("Dolby Vision Profile 7 FEL").
    /// </summary>
    public string DolbyVisionTemplate { get; set; } = string.Empty;

    /// <summary>
    /// Sort steps for <see cref="TrackReorderStrategy.CustomRules"/>, first one wins.
    /// Empty means <see cref="DefaultSortKeys"/>.
    /// </summary>
    public List<TrackSortKey> SortKeys { get; set; } = [];

    /// <summary>Subtitle codecs in the order <see cref="TrackSortKey.CodecOrder"/> places them. Unlisted codecs go last.</summary>
    public List<SubtitleCodec> SubtitleCodecOrder { get; set; } = [];

    /// <summary>Audio codecs in the order <see cref="TrackSortKey.CodecOrder"/> places them. Unlisted codecs go last.</summary>
    public List<AudioCodec> AudioCodecOrder { get; set; } = [];

    public static readonly IReadOnlyList<TrackSortKey> DefaultSortKeys =
        [TrackSortKey.ForcedFirst, TrackSortKey.LanguagePriority, TrackSortKey.CodecOrder];

    [System.Text.Json.Serialization.JsonIgnore]
    public IReadOnlyList<TrackSortKey> EffectiveSortKeys =>
        SortKeys.Count > 0 ? SortKeys.Distinct().ToList() : DefaultSortKeys;
    public bool ExcludeCodecs { get; set; }
    public List<SubtitleCodec> ExcludedCodecs { get; set; } = [];
    public List<AudioCodec> ExcludedAudioCodecs { get; set; } = [];

    /// <summary>
    /// Finds the first flag-specific override that applies to the track.
    /// Flags are checked in enum order (SDH > Forced > Commentary > AD).
    /// Empty-string entries count as "no override" (user cleared the box).
    /// </summary>
    public bool TryGetMatchingOverride(IMediaTrack track, out string template)
    {
        foreach (var flag in TrackFlagExtensions.All)
        {
            if (flag.Matches(track)
                && TrackNameOverrides.TryGetValue(flag, out var overrideTemplate)
                && !string.IsNullOrEmpty(overrideTemplate))
            {
                template = overrideTemplate;
                return true;
            }
        }

        template = string.Empty;
        return false;
    }

    /// <summary>
    /// Returns the first matching flag-specific override, or the default template.
    /// </summary>
    public string ResolveTemplate(IMediaTrack track)
    {
        return TryGetMatchingOverride(track, out var overrideTemplate) ? overrideTemplate : TrackNameTemplate;
    }
}

public class ProfileConfiguration : AuditEntityConfiguration<Profile>
{
    public override void Configure(EntityTypeBuilder<Profile> builder)
    {
        base.Configure(builder);

        builder.ToTable(nameof(Profile));
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .IsRequired();

        builder.Property(e => e.ClearVideoTrackNames)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(e => e.ClearFileTitle)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(e => e.RemoveChapters)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(e => e.TrimToVideoLength)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(e => e.SkipHardlinkedFiles)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(e => e.BackupTrackNames)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(e => e.AudioSettings)
            .HasJsonConversion();

        builder.Property(e => e.SubtitleSettings)
            .HasJsonConversion();

        builder.Property(e => e.VideoSettings)
            .HasJsonConversion();
    }
}
