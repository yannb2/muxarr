using Muxarr.Core.Extensions;
using Muxarr.Core.Models;
using Muxarr.Data.Entities;
using Muxarr.Data.Extensions;

namespace Muxarr.Web.Services;

// Validates a converted file against what we asked the writer to produce.
// Catches container flips, track count or ordering drift, and truncation.
public static class OutputValidator
{
    // measuredEnds null means the probe could not run, so there is nothing to hold
    // the output to and the duration check is skipped rather than failed.
    public static void ValidateOrThrow(MediaFile actual, MediaFile source, ConversionPlan target,
        IReadOnlyDictionary<int, long>? measuredEnds = null)
    {
        var actualFamily = actual.Snapshot.ContainerType.ToContainerFamily();
        var sourceFamily = source.Snapshot.ContainerType.ToContainerFamily();
        if (actualFamily != sourceFamily)
        {
            throw new Exception(
                $"Output container family is {actualFamily}, expected {sourceFamily}.");
        }

        var actualTracks = actual.Snapshot.Tracks;
        if (actualTracks.Count != target.Tracks.Count)
        {
            throw new Exception(
                $"Output has {actualTracks.Count} tracks, expected {target.Tracks.Count}.");
        }

        for (var i = 0; i < target.Tracks.Count; i++)
        {
            if (actualTracks[i].Type != target.Tracks[i].Type)
            {
                throw new Exception(
                    $"Output track at position {i} is {actualTracks[i].Type}, expected {target.Tracks[i].Type}.");
            }
        }

        ValidateVideoFormats(actual, source, target);

        var expectedDuration = ExpectedDurationMs(source, target, measuredEnds);
        if (expectedDuration > 0)
        {
            var tolerance = Math.Max(500, expectedDuration / 100);
            if (actual.Snapshot.DurationMs < expectedDuration - tolerance)
            {
                throw new Exception(
                    $"Output duration {actual.Snapshot.DurationMs}ms is shorter than the expected {expectedDuration}ms " +
                    $"(tolerance {tolerance}ms). File may be truncated.");
            }
        }

        if (actual.HasScanWarning && !source.HasScanWarning)
        {
            throw new Exception(
                "ffprobe flagged the output file with a warning that was not present on the source.");
        }

        // Only set when the plan asked to change the container title (currently
        // clear only). Confirms the writer actually applied it.
        if (target.Title != null &&
            !string.Equals(actual.Snapshot.Title ?? "", target.Title, StringComparison.Ordinal))
        {
            throw new Exception(
                $"Output container title is '{actual.Snapshot.Title}', expected '{target.Title}'.");
        }

        if (target.HasChapters == false && actual.Snapshot.HasChapters)
        {
            throw new Exception("Output still has chapters; removal did not apply.");
        }

        // Fork, from Ripparr's verify step: chapters the plan keeps must still be there.
        if (target.HasChapters != false && source.Snapshot.HasChapters && !actual.Snapshot.HasChapters)
        {
            throw new Exception("Output lost the source's chapters. Refusing to replace the original.");
        }
    }

    // Fork, from Ripparr's verify step: the names, languages and flags the plan
    // asked for, compared with what came out (Matroska only: MP4 has no place for
    // most flags). A difference is logged as a warning, not a failure: the file
    // plays, a player only shows or picks something else than planned.
    public static List<string> MetadataDifferences(MediaFile actual, ConversionPlan target)
    {
        var differences = new List<string>();
        if (actual.Snapshot.ContainerType.ToContainerFamily() != ContainerFamily.Matroska)
        {
            return differences;
        }

        var tracks = actual.Snapshot.Tracks;
        for (var i = 0; i < target.Tracks.Count && i < tracks.Count; i++)
        {
            var want = target.Tracks[i];
            var got = tracks[i];
            var words = new List<string>();
            if (want.Name != null && !string.Equals(want.Name.Trim(), (got.Name ?? "").Trim(), StringComparison.Ordinal))
            {
                words.Add($"name '{got.Name}' instead of '{want.Name}'");
            }

            if (!string.IsNullOrEmpty(want.LanguageCode)
                && !string.Equals(want.LanguageCode, got.LanguageCode, StringComparison.OrdinalIgnoreCase))
            {
                words.Add($"language '{got.LanguageCode}' instead of '{want.LanguageCode}'");
            }

            Flag(words, "default", want.IsDefault, got.IsDefault);
            Flag(words, "forced", want.IsForced, got.IsForced);
            Flag(words, "SDH", want.IsHearingImpaired, got.IsHearingImpaired);
            Flag(words, "AD", want.IsVisualImpaired, got.IsVisualImpaired);
            Flag(words, "commentary", want.IsCommentary, got.IsCommentary);
            if (words.Count > 0)
            {
                differences.Add($"Track {i} ({got.Type}): {string.Join(", ", words)}");
            }
        }

        return differences;
    }

    private static void Flag(List<string> words, string name, bool? want, bool got)
    {
        if (want is { } wanted && wanted != got)
        {
            words.Add($"{name} {(got ? "on" : "off")}, planned {(wanted ? "on" : "off")}");
        }
    }

    // A remux is a stream copy, so the HDR format, Dolby Vision profile and
    // enhancement layer of every kept video track must come out as they went
    // in. Losing the FEL of a profile 7 remux is silent in every player that
    // does not use it, so it is caught here, before the original is replaced.
    public static void ValidateVideoFormats(MediaFile actual, MediaFile source, ConversionPlan target)
    {
        var sourceByIndex = source.Snapshot.Tracks
            .GroupBy(t => t.Index)
            .ToDictionary(g => g.Key, g => g.First());
        var keptVideo = target.Tracks
            .Where(t => t.Type == MediaTrackType.Video)
            .Select(t => sourceByIndex.GetValueOrDefault(t.Index))
            .ToList();
        var outputVideo = actual.Snapshot.Tracks.Where(t => t.Type == MediaTrackType.Video).ToList();

        for (var i = 0; i < keptVideo.Count && i < outputVideo.Count; i++)
        {
            var before = keptVideo[i];
            var after = outputVideo[i];
            if (before == null)
            {
                continue;
            }

            // Only what the source is known to have: a snapshot from before
            // detection existed has nulls, and a stream copy cannot add DV.
            if (before.DvProfile != null && !string.Equals(before.DvProfile, after.DvProfile, StringComparison.Ordinal))
            {
                throw new Exception(
                    $"Output video track {i} has Dolby Vision profile '{after.DvProfile ?? "none"}', " +
                    $"the source had '{before.DvProfile ?? "none"}'. Refusing to replace the original.");
            }

            if (before.DvLayer != null && !string.Equals(before.DvLayer, after.DvLayer, StringComparison.Ordinal))
            {
                throw new Exception(
                    $"Output video track {i} has Dolby Vision layer '{after.DvLayer ?? "none found"}', " +
                    $"the source had '{before.DvLayer}'. Refusing to replace the original.");
            }

            if (before.HdrFormat != null && !string.Equals(before.HdrFormat, after.HdrFormat, StringComparison.Ordinal))
            {
                throw new Exception(
                    $"Output video track {i} is '{after.HdrFormat ?? "SDR"}', the source was " +
                    $"'{before.HdrFormat}'. Refusing to replace the original.");
            }
        }
    }

    // The ffmpeg writer's trim cut: the first kept video track's end, from the
    // same sources the validator holds the output to. Null when unknowable.
    public static long? TrimCutMs(MediaFile source, ConversionPlan target,
        IReadOnlyDictionary<int, long>? measured)
    {
        var video = source.Snapshot.Tracks
            .Where(t => t.IsAllowed(target.Tracks))
            .GetVideoTracks().OrderBy(t => t.Index).FirstOrDefault();
        var end = video == null ? 0 : DurationOf(video, measured);
        return end > 0 ? end : null;
    }

    // A container is as long as its longest track, so dropping a track that ran
    // past the rest legitimately shortens the output. Measure against the tracks
    // the plan keeps. Returns 0 to skip the check when nothing can be measured.
    private static long ExpectedDurationMs(MediaFile source, ConversionPlan target,
        IReadOnlyDictionary<int, long>? measured)
    {
        if (source.Snapshot.DurationMs <= 0)
        {
            return 0;
        }

        var kept = source.Snapshot.Tracks.Where(t => t.IsAllowed(target.Tracks)).ToList();

        if (target.TrimToVideoLength)
        {
            // No video means nothing to trim to, so there is no floor to demand.
            var video = kept.GetVideoTracks().OrderBy(t => t.Index).FirstOrDefault();
            return video == null ? 0 : Floor(DurationOf(video, measured), measured, "the video track");
        }

        if (kept.Count == source.Snapshot.Tracks.Count)
        {
            return source.Snapshot.DurationMs;
        }

        // A dropped track may be what set the container's length, so it is no
        // longer the yardstick; a kept track that reports one is still a floor.
        var longest = kept.Select(t => DurationOf(t, measured)).DefaultIfEmpty(0).Max();
        return Floor(longest, measured, "any kept track");
    }

    private static long DurationOf(TrackSnapshot track, IReadOnlyDictionary<int, long>? measured)
    {
        return track.DurationMs > 0 ? track.DurationMs
            : measured != null && measured.TryGetValue(track.Index, out var end) ? end
            : 0;
    }

    // A null measurement means the probe could not run, so there is nothing to hold
    // the output to. An empty one means it ran and found nothing, and we retired the
    // container as the yardstick, so the swap would go unchecked. Fail instead.
    private static long Floor(long durationMs, IReadOnlyDictionary<int, long>? measured, string source)
    {
        if (durationMs > 0 || measured == null)
        {
            return durationMs > 0 ? durationMs : 0;
        }

        throw new Exception(
            $"Could not establish an expected duration from {source}, so the output cannot be "
            + "checked for truncation. Refusing to replace the original.");
    }
}
