using Microsoft.Extensions.Logging;
using Muxarr.Core.MkvToolNix;
using Muxarr.Core.Models;
using Muxarr.Core.Utilities;
using Muxarr.Data.Entities;

namespace Muxarr.Data.Extensions;

/// <summary>
/// Keeps the track names a file arrived with. One hidden <see cref="SidecarName"/>
/// per folder holds an entry per media file; the database keeps a copy on the
/// file's row. Entries follow a file by name, and by size and duration when an
/// arr renames it, so a new release under a new name starts a fresh entry.
/// </summary>
public static class TrackNameBackups
{
    public const string SidecarName = ".muxarr-tracks.json";

    /// <summary>How long an entry whose file is gone is kept, in case the file comes back.</summary>
    public static readonly TimeSpan KeepMissingFor = TimeSpan.FromDays(30);

    /// <summary>An unchanged file only touches its sidecar this often, to keep its entry from being pruned.</summary>
    public static readonly TimeSpan RefreshAfter = TimeSpan.FromDays(7);

    // A remux by muxarr can trim a few frames; a different release of the same
    // title almost never lands this close.
    private const long DurationToleranceMs = 5_000;

    /// <summary>True when the stored copy no longer describes the file and the sidecar needs a visit.</summary>
    public static bool NeedsSync(TrackNameBackup? stored, string path, long size, DateTime now)
    {
        return stored == null
               || stored.FileName != Path.GetFileName(path)
               || stored.Size != size
               || now - stored.LastSeen > RefreshAfter;
    }

    /// <summary>
    /// Finds or creates the entry for the file at <paramref name="path"/>, writes the
    /// folder's sidecar and returns the entry for the database. Names are only ever
    /// taken once: from the sidecar, then the stored copy, then
    /// <paramref name="firstSeenTracks"/> (the file before muxarr first converted it),
    /// then the tracks as they are now.
    /// </summary>
    public static TrackNameBackup Sync(string path, long size, long durationMs,
        IReadOnlyList<IMediaTrack> currentTracks, IReadOnlyList<IMediaTrack>? firstSeenTracks,
        TrackNameBackup? stored, DateTime now, ILogger? logger = null)
    {
        var directory = Path.GetDirectoryName(path) ?? ".";
        var fileName = Path.GetFileName(path);
        var sidecar = Load(directory, logger);

        bool Exists(string name) => File.Exists(Path.Combine(directory, name));

        var entry = sidecar == null ? null : FindEntry(sidecar, fileName, size, durationMs, Exists);
        if (entry == null && stored != null && SameFile(stored, fileName, size, durationMs))
        {
            entry = Copy(stored);
            sidecar?.Files.Add(entry);
        }

        if (entry == null)
        {
            entry = Capture(firstSeenTracks ?? currentTracks, now);
            sidecar?.Files.Add(entry);
        }

        // A different file under this name (a new release kept the old name) replaces the old entry.
        sidecar?.Files.RemoveAll(e => e.FileName == fileName && !ReferenceEquals(e, entry));

        entry.FileName = fileName;
        entry.Size = size;
        entry.DurationMs = durationMs;
        entry.LastSeen = now;

        if (sidecar != null)
        {
            Prune(sidecar, Exists, now);
            Save(directory, sidecar, logger);
        }

        return Copy(entry);
    }

    public static TrackNameBackup Capture(IEnumerable<IMediaTrack> tracks, DateTime now)
    {
        return new TrackNameBackup
        {
            FirstSeen = now,
            LastSeen = now,
            Tracks = tracks
                .OrderBy(t => t.Index)
                .Select(t => new OriginalTrack
                {
                    Index = t.Index,
                    Type = t.Type,
                    Codec = t.Codec,
                    LanguageCode = t.LanguageCode,
                    LanguageName = t.LanguageName,
                    AudioChannels = t.AudioChannels,
                    // Flags as muxarr will read them, name included, so the
                    // converted track still pairs up after the name is gone.
                    IsForced = t.IsForced || TrackNameFlags.ContainsForced(t.Name),
                    IsHearingImpaired = t.IsHearingImpaired || TrackNameFlags.ContainsHearingImpaired(t.Name),
                    IsCommentary = t.IsCommentary || TrackNameFlags.ContainsCommentary(t.Name),
                    Name = string.IsNullOrWhiteSpace(t.Name) ? null : t.Name
                })
                .ToList()
        };
    }

    /// <summary>
    /// The entry for a file: one under the same name, else one whose file is gone
    /// and which matches on size and duration (an arr rename keeps both).
    /// </summary>
    public static TrackNameBackup? FindEntry(TrackNameSidecar sidecar, string fileName, long size, long durationMs,
        Func<string, bool> fileExists)
    {
        var byName = sidecar.Files.FirstOrDefault(e => e.FileName == fileName);
        if (byName != null)
        {
            return SameFile(byName, fileName, size, durationMs) ? byName : null;
        }

        return sidecar.Files.FirstOrDefault(e =>
            e.Size == size && CloseDuration(e.DurationMs, durationMs) && !fileExists(e.FileName));
    }

    /// <summary>
    /// Same name and near the same length is the same file, even after a remux
    /// changed its size. Same size and length is the same file under a new name.
    /// </summary>
    private static bool SameFile(TrackNameBackup entry, string fileName, long size, long durationMs)
    {
        if (!CloseDuration(entry.DurationMs, durationMs))
        {
            return false;
        }

        return entry.FileName == fileName || entry.Size == size;
    }

    private static bool CloseDuration(long a, long b)
    {
        return a <= 0 || b <= 0 || Math.Abs(a - b) <= DurationToleranceMs;
    }

    /// <summary>Drops entries whose file has been gone for longer than <see cref="KeepMissingFor"/>.</summary>
    public static int Prune(TrackNameSidecar sidecar, Func<string, bool> fileExists, DateTime now)
    {
        return sidecar.Files.RemoveAll(e => now - e.LastSeen > KeepMissingFor && !fileExists(e.FileName));
    }

    /// <summary>
    /// Pairs each current track with the original it came from, by index. Tracks
    /// may have been dropped, reordered, relabelled or flagged since, so the
    /// match loosens in passes and takes originals in their source order.
    /// </summary>
    public static Dictionary<int, OriginalTrack> MatchTracks(IEnumerable<IMediaTrack> current,
        IReadOnlyList<OriginalTrack>? originals)
    {
        var result = new Dictionary<int, OriginalTrack>();
        if (originals is not { Count: > 0 })
        {
            return result;
        }

        var tracks = current.OrderBy(t => t.Index).ToList();
        var used = new HashSet<OriginalTrack>(ReferenceEqualityComparer.Instance);

        Func<IMediaTrack, OriginalTrack, bool>[] passes =
        [
            (t, o) => t.Type == o.Type && t.Codec == o.Codec && t.AudioChannels == o.AudioChannels
                      && string.Equals(t.LanguageCode, o.LanguageCode, StringComparison.OrdinalIgnoreCase)
                      && t.IsForced == o.IsForced && t.IsHearingImpaired == o.IsHearingImpaired
                      && t.IsCommentary == o.IsCommentary,
            // Language resolved from "und", SDH read off the name.
            (t, o) => t.Type == o.Type && t.Codec == o.Codec && t.AudioChannels == o.AudioChannels
                      && t.IsForced == o.IsForced && t.IsCommentary == o.IsCommentary,
            (t, o) => t.Type == o.Type && t.Codec == o.Codec && t.AudioChannels == o.AudioChannels
        ];

        foreach (var pass in passes)
        {
            foreach (var track in tracks.Where(t => !result.ContainsKey(t.Index)))
            {
                var match = originals.FirstOrDefault(o => !used.Contains(o) && pass(track, o));
                if (match != null)
                {
                    result[track.Index] = match;
                    used.Add(match);
                }
            }
        }

        return result;
    }

    /// <summary>Sets <see cref="TrackSnapshot.OriginalName"/> on copies of the file's tracks.</summary>
    public static void AttachOriginalNames(this IEnumerable<TrackSnapshot> tracks, TrackNameBackup? backup,
        IEnumerable<IMediaTrack> allTracks)
    {
        if (backup is not { Tracks.Count: > 0 })
        {
            return;
        }

        var matches = MatchTracks(allTracks, backup.Tracks);
        foreach (var track in tracks)
        {
            track.OriginalName = matches.TryGetValue(track.Index, out var original) ? original.Name : null;
        }
    }

    /// <summary>The original name of one track, or null when unknown.</summary>
    public static string? OriginalNameFor(this TrackNameBackup? backup, IMediaTrack track,
        IEnumerable<IMediaTrack> allTracks)
    {
        if (backup == null)
        {
            return null;
        }

        return MatchTracks(allTracks, backup.Tracks).TryGetValue(track.Index, out var original)
            ? original.Name
            : null;
    }

    /// <summary>Reads the folder's sidecar. Missing gives an empty one; unreadable gives null so it is never overwritten.</summary>
    public static TrackNameSidecar? Load(string directory, ILogger? logger = null)
    {
        var file = Path.Combine(directory, SidecarName);
        if (!File.Exists(file))
        {
            return Directory.Exists(directory) ? new TrackNameSidecar() : null;
        }

        try
        {
            return JsonHelper.Deserialize<TrackNameSidecar>(File.ReadAllText(file)) ?? new TrackNameSidecar();
        }
        catch (Exception e)
        {
            logger?.LogWarning(e, "Track name backup '{File}' could not be read; leaving it untouched", file);
            return null;
        }
    }

    public static void Save(string directory, TrackNameSidecar sidecar, ILogger? logger = null)
    {
        var file = Path.Combine(directory, SidecarName);
        var temp = file + ".tmp";

        try
        {
            if (sidecar.Files.Count == 0)
            {
                File.Delete(file);
                return;
            }

            File.WriteAllText(temp, JsonHelper.SerializeIndented(sidecar));
            File.Move(temp, file, true);
        }
        catch (Exception e)
        {
            logger?.LogWarning(e, "Track name backup '{File}' could not be written", file);
            try
            {
                File.Delete(temp);
            }
            catch
            {
                // Nothing more to do; the next scan tries again.
            }
        }
    }

    private static TrackNameBackup Copy(TrackNameBackup source)
    {
        return JsonHelper.Deserialize<TrackNameBackup>(JsonHelper.Serialize(source))!;
    }
}
