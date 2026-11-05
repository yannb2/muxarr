using System.Globalization;
using Arrkit.Processes;
using Arrkit.Subtitles;
using Muxarr.Core.SubtitleCopy;

namespace Muxarr.Web.Services.SubtitleCopy;

/// <summary>
/// Reads subtitle tracks for the Copy subtitles page: only the start times for the
/// timing check (ffprobe's packet list, any codec, any container), or the captions
/// themselves for the side-by-side preview.
/// </summary>
public static class CueReader
{
    public static string Ffprobe { get; set; } = "ffprobe";
    public static string Ffmpeg { get; set; } = "ffmpeg";

    // A PGS caption is two display sets: one that shows the picture and one that
    // clears it. The clearing one has no picture, so it is a few dozen bytes.
    private const int PgsClearMaxBytes = 80;

    public sealed record Window(long FromMs, long ToMs)
    {
        public bool Contains(long ms) => ms >= FromMs && ms <= ToMs;
    }

    /// <summary>
    /// The start time (ms) of every caption of the track in the windows, or in the whole
    /// file when there are none. <paramref name="streamIndex"/> is the stream number.
    /// </summary>
    public static async Task<List<long>> StartsAsync(string path, int streamIndex, string? codec,
        IReadOnlyList<Window>? windows, CancellationToken token)
    {
        var arguments = new List<string>
        {
            "-v", "error", "-select_streams", streamIndex.ToString(CultureInfo.InvariantCulture),
            "-show_entries", "packet=pts_time,size", "-of", "csv=p=0"
        };
        if (windows is { Count: > 0 })
        {
            arguments.Add("-read_intervals");
            arguments.Add(string.Join(",", windows.Select(w => $"{Seconds(w.FromMs)}%{Seconds(w.ToMs)}")));
        }

        arguments.Add(path);
        var result = await ToolRunner.RunAsync(Ffprobe, arguments, TimeSpan.FromMinutes(5), cancellationToken: token);
        if (!result.Success)
        {
            throw new InvalidOperationException(result.NotStarted
                ? "ffprobe is not installed."
                : $"ffprobe could not read the subtitles of {Path.GetFileName(path)}: {result.LastLine}");
        }

        var pictures = IsPgs(codec, path);
        var starts = new SortedSet<long>();
        foreach (var line in result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = line.Split(',');
            if (parts.Length < 2
                || !double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)
                || !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var size))
            {
                continue;
            }

            if (pictures && size <= PgsClearMaxBytes)
            {
                continue;
            }

            var ms = (long)Math.Round(seconds * 1000);
            if (windows is not { Count: > 0 } || windows.Any(w => w.Contains(ms)))
            {
                starts.Add(ms);
            }
        }

        return starts.ToList();
    }

    public static bool IsPgs(string? codec, string path) =>
        Path.GetExtension(path).Equals(".sup", StringComparison.OrdinalIgnoreCase)
        || codec != null && (codec.Contains("pgs", StringComparison.OrdinalIgnoreCase) || codec.Contains("hdmv", StringComparison.OrdinalIgnoreCase));

    /// <summary>One caption for the preview: its text, or its picture as a PNG data URL.</summary>
    public sealed record Caption(long StartMs, long EndMs, string? Text, string? Picture);

    /// <summary>The captions of the track between the two times (ms), in the file's own times.</summary>
    public static async Task<List<Caption>> CaptionsAsync(string path, int streamIndex, string? codec,
        long fromMs, long toMs, string workFolder, CancellationToken token)
    {
        Directory.CreateDirectory(workFolder);
        if (TrackSampler.IsUnreadablePictures(codec))
        {
            throw new InvalidOperationException("DVD picture subtitles have no preview.");
        }

        var pictures = IsPgs(codec, path);
        List<Caption> captions;
        if (SubtitleFiles.IsLooseSubtitle(path))
        {
            captions = await LooseAsync(path, pictures, workFolder, token);
        }
        else
        {
            var output = Path.Combine(workFolder, $"preview-{Guid.NewGuid():N}{(pictures ? ".sup" : ".srt")}");
            var arguments = new List<string>
            {
                "-v", "error", "-nostdin", "-y", "-copyts",
                "-ss", Seconds(Math.Max(0, fromMs)), "-i", path,
                "-map", $"0:{streamIndex}", "-to", Seconds(toMs)
            };
            arguments.AddRange(pictures ? ["-c", "copy", "-f", "sup"] : ["-c:s", "srt", "-f", "srt"]);
            arguments.Add(output);
            var result = await ToolRunner.RunAsync(Ffmpeg, arguments, TimeSpan.FromMinutes(5), cancellationToken: token);
            if (!result.Success || !File.Exists(output))
            {
                throw new InvalidOperationException(result.NotStarted
                    ? "ffmpeg is not installed."
                    : $"ffmpeg could not read the track: {result.LastLine}");
            }

            captions = pictures
                ? FromPictures(PgsReader.Read(await File.ReadAllBytesAsync(output, token)))
                : FromCues(TrackSampler.ParseSrt(await File.ReadAllTextAsync(output, token)));
            File.Delete(output);
        }

        return captions.Where(c => c.StartMs >= fromMs && c.StartMs <= toMs).OrderBy(c => c.StartMs).ToList();
    }

    private static async Task<List<Caption>> LooseAsync(string path, bool pictures, string workFolder, CancellationToken token)
    {
        if (pictures)
        {
            return FromPictures(PgsReader.Read(await File.ReadAllBytesAsync(path, token)));
        }

        if (Path.GetExtension(path).Equals(".srt", StringComparison.OrdinalIgnoreCase))
        {
            return FromCues(TrackSampler.ParseSrt(SubtitleFiles.ReadText(path)));
        }

        // .ass/.ssa: ffmpeg turns the styled events into plain SRT cues.
        var output = Path.Combine(workFolder, $"preview-{Guid.NewGuid():N}.srt");
        var result = await ToolRunner.RunAsync(Ffmpeg, ["-v", "error", "-nostdin", "-y", "-i", path, "-c:s", "srt", "-f", "srt", output],
            TimeSpan.FromMinutes(2), cancellationToken: token);
        if (!result.Success || !File.Exists(output))
        {
            throw new InvalidOperationException($"ffmpeg could not read {Path.GetFileName(path)}: {result.LastLine}");
        }

        var cues = TrackSampler.ParseSrt(await File.ReadAllTextAsync(output, token));
        File.Delete(output);
        return FromCues(cues);
    }

    private static List<Caption> FromCues(IEnumerable<SrtCue> cues) =>
        cues.Select(c => new Caption((long)c.Start.TotalMilliseconds, (long)c.End.TotalMilliseconds, c.Text, null)).ToList();

    private static List<Caption> FromPictures(IEnumerable<PgsCaption> captions) =>
        captions.Where(c => c.Objects.Count > 0)
            .GroupBy(c => c.Start).Select(g => g.First())
            .Select(c => new Caption((long)c.Start.TotalMilliseconds, (long)c.End.TotalMilliseconds, null,
                "data:image/png;base64," + Convert.ToBase64String(Png.Rgba(c.Render()))))
            .ToList();

    private static string Seconds(long ms) => (ms / 1000.0).ToString("0.###", CultureInfo.InvariantCulture);
}
