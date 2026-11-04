using System.Globalization;
using System.Text.RegularExpressions;
using Muxarr.Core.Utilities;

namespace Muxarr.Core.Subtitles;

/// <summary>Captions of a subtitle track, read from short slices spread over the file.</summary>
public sealed class SubtitleSample
{
    /// <summary>Picture captions (PGS), or empty for a text track.</summary>
    public List<PgsCaption> Pictures { get; init; } = [];

    /// <summary>Text captions (SRT, ASS...), or empty for a picture track.</summary>
    public List<SrtCue> Texts { get; init; } = [];

    public int Count => Pictures.Count + Texts.Count;
}

/// <summary>
/// Reads a few slices of one track with ffmpeg instead of the whole file: a remux is tens of
/// gigabytes, while the preview, the SDH and commentary check and Listen only need a sample.
/// </summary>
public static partial class TrackSampler
{
    internal const string Ffmpeg = "ffmpeg";

    /// <summary>How many slices, spread over the film, and how long each one is.</summary>
    public const int Slices = 12;

    public static readonly TimeSpan SliceLength = TimeSpan.FromSeconds(40);

    /// <summary>Where each slice starts: evenly spread, skipping the first and last few minutes (titles, credits).</summary>
    public static List<TimeSpan> SliceStarts(long durationMs, int count = Slices)
    {
        var duration = TimeSpan.FromMilliseconds(Math.Max(0, durationMs));
        if (duration <= SliceLength * count)
        {
            return [TimeSpan.Zero];
        }

        var edge = duration > TimeSpan.FromMinutes(30) ? TimeSpan.FromMinutes(3) : TimeSpan.Zero;
        var span = duration - edge - edge - SliceLength;
        return Enumerable.Range(0, count)
            .Select(i => edge + span * (i + 0.5) / count)
            .Select(t => TimeSpan.FromSeconds(Math.Floor(t.TotalSeconds)))
            .ToList();
    }

    /// <summary>Picture subtitles are PGS; everything else is read as text.</summary>
    public static bool IsPictures(string? codec) =>
        codec != null && (codec.Contains("pgs", StringComparison.OrdinalIgnoreCase) || codec.Contains("hdmv", StringComparison.OrdinalIgnoreCase));

    /// <summary>VobSub and DVB are pictures too, but not PGS: they can't be previewed or read here.</summary>
    public static bool IsUnreadablePictures(string? codec) =>
        codec != null && (codec.Contains("vob", StringComparison.OrdinalIgnoreCase) || codec.Contains("dvd", StringComparison.OrdinalIgnoreCase)
                                                                                     || codec.Contains("dvb", StringComparison.OrdinalIgnoreCase));

    /// <summary>The captions of a subtitle track in <see cref="Slices"/> slices of the file.</summary>
    /// <param name="index">The stream index (ffmpeg's 0:N).</param>
    public static async Task<SubtitleSample> SubtitlesAsync(string file, int index, string? codec, long durationMs,
        string workFolder, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(workFolder);
        var pictures = IsPictures(codec);
        var captions = new List<PgsCaption>();
        var cues = new List<SrtCue>();
        var starts = SliceStarts(durationMs);
        var n = 0;
        foreach (var start in starts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var output = Path.Combine(workFolder, $"slice-{n++:D2}{(pictures ? ".sup" : ".srt")}");
            var length = starts.Count == 1 ? TimeSpan.FromMilliseconds(Math.Max(durationMs, 1)) : SliceLength;
            // -copyts keeps the film's own times, so the captions say where they are. With it, an
            // input -t doesn't stop the read: the output -to (in those same times) does.
            var arguments = new List<string>
            {
                "-v", "error", "-nostdin", "-y", "-copyts",
                "-ss", Seconds(start), "-i", file,
                "-map", $"0:{index}", "-to", Seconds(start + length)
            };
            arguments.AddRange(pictures ? ["-c", "copy", "-f", "sup"] : ["-c:s", "srt", "-f", "srt"]);
            arguments.Add(output);

            var result = await ProcessExecutor.ExecuteProcessAsync(Ffmpeg, timeout: TimeSpan.FromMinutes(5), argumentList: arguments);
            if (!result.Success || !File.Exists(output))
            {
                var last = (result.Error ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries).LastOrDefault()?.Trim();
                throw new InvalidOperationException($"ffmpeg could not read the track at {Seconds(start)} s: {last}");
            }

            if (pictures)
            {
                captions.AddRange(PgsReader.Read(await File.ReadAllBytesAsync(output, cancellationToken)));
            }
            else
            {
                cues.AddRange(ParseSrt(await File.ReadAllTextAsync(output, cancellationToken)));
            }

            File.Delete(output);
        }

        return new SubtitleSample
        {
            Pictures = captions.Where(c => c.Objects.Count > 0).GroupBy(c => c.Start).Select(g => g.First()).OrderBy(c => c.Start).ToList(),
            Texts = cues.GroupBy(c => c.Start).Select(g => g.First()).OrderBy(c => c.Start).ToList()
        };
    }

    /// <summary>
    /// The captions as the SDH and commentary check reads them: text tracks as they are, picture
    /// tracks through Tesseract in the track's language.
    /// </summary>
    public static async Task<List<OcrCaption>> ReadAsync(SubtitleSample sample, string? language, string tesseract,
        string workFolder, CancellationToken cancellationToken)
    {
        if (sample.Pictures.Count == 0)
        {
            return sample.Texts.Select((c, i) => new OcrCaption(i, c.Start, c.End, false, c.Text, SdhCleaner.Clean(c.Text))).ToList();
        }

        var picked = SubtitleClassifier.Sample(sample.Pictures);
        return await SubtitleOcr.RecogniseAsync(picked, tesseract, SubtitleOcr.TesseractLanguage(language), workFolder, null, cancellationToken);
    }

    /// <summary>20 seconds of an audio track as AAC stereo (.m4a), from a third of the way in.</summary>
    public static async Task<byte[]> ListenAsync(string file, int index, long durationMs, string workFolder,
        TimeSpan? at, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(workFolder);
        var start = at ?? (durationMs > 120_000 ? TimeSpan.FromMilliseconds(durationMs / 3.0) : TimeSpan.Zero);
        var output = Path.Combine(workFolder, $"listen-{Guid.NewGuid():N}.m4a");
        try
        {
            var result = await ProcessExecutor.ExecuteProcessAsync(Ffmpeg, timeout: TimeSpan.FromMinutes(2), argumentList:
            [
                "-v", "error", "-nostdin", "-y", "-ss", Seconds(start), "-t", "20", "-i", file,
                "-map", $"0:{index}", "-vn", "-ac", "2", "-c:a", "aac", "-b:a", "128k", "-movflags", "+faststart", output
            ]);
            if (!result.Success || !File.Exists(output))
            {
                var last = (result.Error ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries).LastOrDefault()?.Trim();
                throw new InvalidOperationException($"ffmpeg could not read the track: {last}");
            }

            return await File.ReadAllBytesAsync(output, cancellationToken);
        }
        finally
        {
            File.Delete(output);
        }
    }

    /// <summary>The cues of an SRT file (ffmpeg's output: no styling, one blank line between cues).</summary>
    public static List<SrtCue> ParseSrt(string text)
    {
        var cues = new List<SrtCue>();
        foreach (var block in Regex.Split(text.Replace("\r", ""), @"\n{2,}"))
        {
            var lines = block.Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList();
            var timing = lines.FindIndex(l => l.Contains("-->", StringComparison.Ordinal));
            if (timing < 0)
            {
                continue;
            }

            var match = TimingRegex().Match(lines[timing]);
            if (!match.Success)
            {
                continue;
            }

            var body = string.Join('\n', lines.Skip(timing + 1).Select(l => TagRegex().Replace(l, "").Trim()).Where(l => l.Length > 0));
            if (body.Length > 0)
            {
                cues.Add(new SrtCue(Time(match.Groups[1].Value), Time(match.Groups[2].Value), body));
            }
        }

        return cues;
    }

    private static TimeSpan Time(string value) =>
        TimeSpan.ParseExact(value.Replace('.', ','), @"hh\:mm\:ss\,fff", CultureInfo.InvariantCulture);

    private static string Seconds(TimeSpan t) => t.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture);

    [GeneratedRegex(@"(\d{2}:\d{2}:\d{2}[,.]\d{3})\s*-->\s*(\d{2}:\d{2}:\d{2}[,.]\d{3})")]
    private static partial Regex TimingRegex();

    [GeneratedRegex(@"<[^>]+>|\{\\[^}]*\}")]
    private static partial Regex TagRegex();
}
