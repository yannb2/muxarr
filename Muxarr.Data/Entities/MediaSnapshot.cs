using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Muxarr.Core.Extensions;
using Muxarr.Core.Models;

namespace Muxarr.Data.Entities;

// Observation of a probe result. Held alive by incoming FKs
// (MediaFile.SnapshotId, MediaConversion.Before/AfterSnapshotId).
// No owner cascade; orphans need manual cleanup.
public class MediaSnapshot : IMedia<TrackSnapshot>
{
    [CompareIgnore]
    public int Id { get; set; }

    [CompareIgnore]
    public DateTime CapturedAt { get; set; }

    public string? ContainerType { get; set; }
    public string? Title { get; set; }
    public string? Resolution { get; set; }
    public long DurationMs { get; set; }
    public int VideoBitDepth { get; set; }
    public int TrackCount { get; set; }
    public bool HasChapters { get; set; }
    public bool HasAttachments { get; set; }
    public bool HasFaststart { get; set; }

    // A stored snapshot is a probe, so its tracks are in source (Index) order,
    // and the planner compares them position by position. SQL gives no row
    // order without ORDER BY: SQLite picks among the four SnapshotId indexes by
    // estimated cost, and adding columns can flip it to (SnapshotId, Type, ...)
    // order. EF fills the field; reads of a loaded snapshot come back sorted.
    // In-memory snapshots keep the order they were built in (custom reorders).
    private List<TrackSnapshot> _tracks = [];
    private bool _sortTracksOnRead;

    public List<TrackSnapshot> Tracks
    {
        get
        {
            if (_sortTracksOnRead && !IsInIndexOrder(_tracks))
            {
                // Stable, and in place so EF keeps tracking the same list.
                var sorted = _tracks.OrderBy(t => t.Index).ToList();
                _tracks.Clear();
                _tracks.AddRange(sorted);
            }

            return _tracks;
        }
        set
        {
            _tracks = value;
            _sortTracksOnRead = false;
        }
    }

    /// <summary>Called when EF materializes this snapshot from the database.</summary>
    internal void MarkLoadedFromDatabase()
    {
        _sortTracksOnRead = true;
    }

    private static bool IsInIndexOrder(List<TrackSnapshot> tracks)
    {
        for (var i = 1; i < tracks.Count; i++)
        {
            if (tracks[i - 1].Index > tracks[i].Index)
            {
                return false;
            }
        }

        return true;
    }
}

public class TrackSnapshot : IMediaTrack
{
    [CompareIgnore]
    public int Id { get; set; }

    [CompareIgnore]
    public int SnapshotId { get; set; }

    public int Index { get; set; }
    public MediaTrackType Type { get; set; }
    public bool IsCommentary { get; set; }
    public bool IsHearingImpaired { get; set; }
    public bool IsVisualImpaired { get; set; }
    public bool IsDefault { get; set; }
    public bool IsForced { get; set; }
    public bool IsOriginal { get; set; }
    public bool IsDub { get; set; }
    public string Codec { get; set; } = string.Empty;
    public int AudioChannels { get; set; }
    public AudioFeature AudioFeature { get; set; }

    // Video only. HDR10/HLG from the base layer, Dolby Vision profile ("7",
    // "8.1") from the DOVI record, FEL/MEL from dovi_tool. Null when absent.
    public string? HdrFormat { get; set; }
    public string? DvProfile { get; set; }
    public string? DvLayer { get; set; }
    public string LanguageCode { get; set; } = string.Empty;
    public string LanguageName { get; set; } = string.Empty;
    public string? Name { get; set; } = string.Empty;
    public long DurationMs { get; set; }

    [CompareIgnore]
    public MediaSnapshot? Snapshot { get; set; }
}

public class MediaSnapshotConfiguration : IEntityTypeConfiguration<MediaSnapshot>
{
    public void Configure(EntityTypeBuilder<MediaSnapshot> builder)
    {
        builder.ToTable(nameof(MediaSnapshot));

        builder.HasKey(e => e.Id);

        builder.Property(e => e.CapturedAt)
            .IsRequired();

        builder.Property(e => e.ContainerType)
            .HasMaxLength(50);

        builder.Property(e => e.Title)
            .HasMaxLength(500);

        builder.Property(e => e.Resolution)
            .HasMaxLength(20);

        builder.Property(e => e.HasChapters)
            .IsRequired();

        builder.Property(e => e.HasAttachments)
            .IsRequired();

        builder.Property(e => e.HasFaststart)
            .IsRequired();

        builder.Property(e => e.TrackCount)
            .IsRequired();

        builder.HasIndex(e => e.ContainerType);
        builder.HasIndex(e => e.Resolution);

        builder.HasMany(s => s.Tracks)
            .WithOne(t => t.Snapshot)
            .HasForeignKey(t => t.SnapshotId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(s => s.Tracks).AutoInclude();
    }
}

public class TrackSnapshotConfiguration : IEntityTypeConfiguration<TrackSnapshot>
{
    public void Configure(EntityTypeBuilder<TrackSnapshot> builder)
    {
        builder.ToTable(nameof(TrackSnapshot));

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Type)
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(e => e.Codec)
            .HasMaxLength(100);

        builder.Property(e => e.AudioFeature)
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(e => e.HdrFormat)
            .HasMaxLength(20);

        builder.Property(e => e.DvProfile)
            .HasMaxLength(10);

        builder.Property(e => e.DvLayer)
            .HasMaxLength(10);

        builder.Property(e => e.LanguageCode)
            .HasMaxLength(20);

        builder.Property(e => e.LanguageName)
            .HasMaxLength(100);

        builder.Property(e => e.Name)
            .HasMaxLength(500);

        builder.HasIndex(e => new { e.SnapshotId, e.Index }).IsUnique();
        builder.HasIndex(e => new { e.SnapshotId, e.Type, e.Codec });
        builder.HasIndex(e => new { e.SnapshotId, e.Type, e.LanguageName });
        builder.HasIndex(e => new { e.SnapshotId, e.Type, e.AudioChannels });
    }
}
