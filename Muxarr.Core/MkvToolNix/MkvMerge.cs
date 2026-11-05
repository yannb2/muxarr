using System.Diagnostics;
using Muxarr.Core.Models;
using Muxarr.Core.Utilities;

namespace Muxarr.Core.MkvToolNix;

public static class MkvMerge
{
    private const string MkvMergeExecutable = "mkvmerge";

    public const string VideoTrack = "video";
    public const string AudioTrack = "audio";
    public const string SubtitlesTrack = "subtitles";

    // mkvmerge exit codes: 0=success, 1=warnings (still valid), 2=error.
    public static bool IsSuccess(ProcessResult result)
    {
        return result.ExitCode is 0 or 1;
    }

    public static async Task<ProcessJsonResult<MkvMergeInfo>> GetFileInfo(string file)
    {
        var result =
            await ProcessExecutor.ExecuteProcessAsync(MkvMergeExecutable, $"-J \"{file}\"", TimeSpan.FromSeconds(30));
        var json = new ProcessJsonResult<MkvMergeInfo>(result);

        if (!IsSuccess(result) || string.IsNullOrEmpty(result.Output))
        {
            return json;
        }

        try
        {
            json.Result = JsonHelper.Deserialize<MkvMergeInfo>(result.Output);
        }
        catch (Exception e)
        {
            result.Error = e.ToString();
        }

        return json;
    }

    public static async Task<ProcessResult> Remux(string input, string output, ConversionPlan delta,
        Action<string, int>? onProgress = null, TimeSpan? timeout = null)
    {
        var tracks = delta.Tracks;
        if (tracks.Count == 0)
        {
            throw new ArgumentException("At least one track is required.", nameof(delta));
        }

        var audioTracks = tracks.Where(t => t.Type == MediaTrackType.Audio).ToList();
        var subtitleTracks = tracks.Where(t => t.Type == MediaTrackType.Subtitles).ToList();

        var command = $"-o \"{output}\"";

        if (delta.Title != null)
        {
            command += $" --title {MkvToolNixHelper.EscapeValue(delta.Title)}";
        }

        command += audioTracks.Count > 0
            ? $" --audio-tracks {string.Join(",", audioTracks.Select(t => t.Index))}"
            : " --no-audio";

        command += subtitleTracks.Count > 0
            ? $" --subtitle-tracks {string.Join(",", subtitleTracks.Select(t => t.Index))}"
            : " --no-subtitles";

        if (delta.HasChapters == false)
        {
            command += " --no-chapters";
        }
        else if (delta.ChaptersFile != null)
        {
            // The renamed copy replaces the source's chapters rather than adding to them.
            command += $" --chapters \"{delta.ChaptersFile}\" --no-chapters";
        }

        if (delta.TagsFile != null)
        {
            // The edited copy replaces the source's global tags; "" drops them all.
            command += delta.TagsFile.Length == 0
                ? " --no-global-tags"
                : $" --global-tags \"{delta.TagsFile}\" --no-global-tags";
        }

        if (delta.HasAttachments == false)
        {
            command += " --no-attachments";
        }

        if (delta.TrimToVideoLength)
        {
            command += " --stop-after-video-ends";
        }

        foreach (var track in tracks)
        {
            if (track.Name != null)
            {
                command += $" --track-name {track.Index}:{MkvToolNixHelper.EscapeValue(track.Name)}";
            }

            if (track.LanguageCode != null)
            {
                command += $" --language {track.Index}:{track.LanguageCode}";
            }

            if (track.IsDefault != null)
            {
                command += $" --default-track-flag {track.Index}:{(track.IsDefault.Value ? "1" : "0")}";
            }

            if (track.IsForced != null)
            {
                command += $" --forced-display-flag {track.Index}:{(track.IsForced.Value ? "1" : "0")}";
            }

            if (track.IsHearingImpaired != null)
            {
                command +=
                    $" --hearing-impaired-flag {track.Index}:{(track.IsHearingImpaired.Value ? "1" : "0")}";
            }

            if (track.IsVisualImpaired != null)
            {
                command += $" --visual-impaired-flag {track.Index}:{(track.IsVisualImpaired.Value ? "1" : "0")}";
            }

            if (track.IsCommentary != null)
            {
                command += $" --commentary-flag {track.Index}:{(track.IsCommentary.Value ? "1" : "0")}";
            }

            if (track.IsOriginal != null)
            {
                command += $" --original-flag {track.Index}:{(track.IsOriginal.Value ? "1" : "0")}";
            }
        }

        command += $" \"{input}\"";

        // Subtitle files made during the conversion (SRT without SDH), one file each.
        foreach (var added in delta.AddedSubtitles ?? [])
        {
            command += $" --language 0:{added.LanguageCode} --track-name 0:{MkvToolNixHelper.EscapeValue(added.Name)}"
                       + " --default-track-flag 0:0 --forced-display-flag 0:0 --hearing-impaired-flag 0:0"
                       + $" --sub-charset 0:UTF-8 \"{added.Path}\"";
        }

        // Subtitle tracks copied from other files, one input each (the same file
        // may be read more than once), with only that one track taken from it.
        foreach (var donor in delta.Donors ?? [])
        {
            if (donor.Output == null)
            {
                continue;
            }

            var plan = donor.ToTrackPlan(0);
            var id = donor.TrackId;
            command += " --no-video --no-audio --no-buttons --no-chapters --no-attachments --no-global-tags --no-track-tags"
                       + $" --subtitle-tracks {id}"
                       + $" --track-name {id}:{MkvToolNixHelper.EscapeValue(plan.Name ?? "")}"
                       + $" --language {id}:{plan.LanguageCode}"
                       + $" --default-track-flag {id}:{Bit(plan.IsDefault)}"
                       + $" --forced-display-flag {id}:{Bit(plan.IsForced)}"
                       + $" --hearing-impaired-flag {id}:{Bit(plan.IsHearingImpaired)}"
                       + $" --visual-impaired-flag {id}:{Bit(plan.IsVisualImpaired)}"
                       + $" --commentary-flag {id}:{Bit(plan.IsCommentary)}"
                       + $" --original-flag {id}:{Bit(plan.IsOriginal)}";
            if (donor.SyncValue() is { } sync)
            {
                command += $" --sync {id}:{sync}";
            }

            if (SubtitleCopy.SubtitleFiles.CharsetFor(donor.Path) is { } charset)
            {
                command += $" --sub-charset {id}:{charset}";
            }

            command += $" \"{donor.Path}\"";
        }

        command += $" --track-order {string.Join(",", delta.TrackOrder())}";

        var lastProgress = 0;
        return await ProcessExecutor.ExecuteProcessAsync(MkvMergeExecutable, command, timeout,
            OnOutputLine);

        void OnOutputLine(string line, bool error)
        {
            if (line.StartsWith("Progress: ", StringComparison.OrdinalIgnoreCase))
            {
                var percentString = line.Substring("Progress: ".Length).TrimEnd('%');
                if (int.TryParse(percentString, out var progressValue))
                {
                    lastProgress = progressValue;
                }
            }

            onProgress?.Invoke(line, lastProgress);
        }
    }

    private static string Bit(bool? value) => value == true ? "1" : "0";

    public static void KillExistingProcesses()
    {
        var processes = Process.GetProcesses().Where(p =>
        {
            try
            {
                return string.Equals(p.ProcessName, MkvMergeExecutable, StringComparison.CurrentCultureIgnoreCase);
            }
            catch (Exception)
            {
                return false;
            }
        }).ToList();

        foreach (var process in processes)
        {
            try
            {
                process.Kill();
            }
            finally
            {
                process.Dispose();
            }
        }
    }

    public static bool IsHearingImpaired(this Track track)
    {
        return track.Properties.FlagHearingImpaired
               || TrackNameFlags.ContainsHearingImpaired(track.Properties.TrackName);
    }

    public static bool IsVisualImpaired(this Track track)
    {
        return track.Properties.FlagVisualImpaired
               || track.Properties.FlagTextDescriptions
               || TrackNameFlags.ContainsVisualImpaired(track.Properties.TrackName);
    }

    public static bool IsForced(this Track track)
    {
        return track.Properties.ForcedTrack
               || TrackNameFlags.ContainsForced(track.Properties.TrackName);
    }

    public static bool IsOriginal(this Track track)
    {
        return track.Properties.FlagOriginal;
    }

    public static bool IsCommentary(this Track track)
    {
        return track.Properties.FlagCommentary
               || TrackNameFlags.ContainsCommentary(track.Properties.TrackName);
    }

    public static bool IsDub(this Track track)
    {
        return TrackNameFlags.ContainsDub(track.Properties.TrackName);
    }
}
