using System.Text;
using Muxarr.Core.Subtitles;
using Muxarr.Core.Utilities;

namespace Muxarr.Tests;

/// <summary>
/// The track checks on the file page: where the slices go, the SRT reader, and live reads of
/// generated files (these need ffmpeg on PATH).
/// </summary>
[TestClass]
public class TrackSamplerTests
{
    private static string Sup => Path.Combine(AppContext.BaseDirectory, "Fixtures", "captions.sup");
    private static string Mkv => Path.Combine(AppContext.BaseDirectory, "Fixtures", "test.mkv");

    private string _folder = null!;

    [TestInitialize]
    public void Setup()
    {
        _folder = Path.Combine(Path.GetTempPath(), "muxarr-sampler-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, true);
        }
    }

    [TestMethod]
    public void AShortFileIsReadWhole()
    {
        CollectionAssert.AreEqual(new[] { TimeSpan.Zero }, TrackSampler.SliceStarts(60_000));
        CollectionAssert.AreEqual(new[] { TimeSpan.Zero }, TrackSampler.SliceStarts(0));
    }

    [TestMethod]
    public void SlicesSpreadOverAFilmAndSkipTitlesAndCredits()
    {
        var film = TimeSpan.FromHours(2);
        var starts = TrackSampler.SliceStarts((long)film.TotalMilliseconds);

        Assert.HasCount(TrackSampler.Slices, starts);
        Assert.IsTrue(starts[0] >= TimeSpan.FromMinutes(3), "past the opening titles");
        Assert.IsTrue(starts[^1] + TrackSampler.SliceLength <= film - TimeSpan.FromMinutes(3), "before the credits");
        CollectionAssert.AreEqual(starts.OrderBy(s => s).ToList(), starts);
        Assert.AreEqual(starts.Count, starts.Distinct().Count());
    }

    [TestMethod]
    public void TellsPicturesFromText()
    {
        Assert.IsTrue(TrackSampler.IsPictures("Pgs"));
        Assert.IsTrue(TrackSampler.IsPictures("hdmv_pgs_subtitle"));
        Assert.IsFalse(TrackSampler.IsPictures("Srt"));
        Assert.IsFalse(TrackSampler.IsPictures("Ass"));
        Assert.IsTrue(TrackSampler.IsUnreadablePictures("VobSub"));
        Assert.IsTrue(TrackSampler.IsUnreadablePictures("DvbSub"));
        Assert.IsFalse(TrackSampler.IsUnreadablePictures("Pgs"));
    }

    [TestMethod]
    public void ReadsSrt()
    {
        var cues = TrackSampler.ParseSrt(
            "1\r\n00:00:01,000 --> 00:00:03,500\r\n<i>[DOOR OPENS]</i>\r\nHello.\r\n\r\n" +
            "2\n00:01:02.250 --> 00:01:04.000\n{\\an8}Up here\n\n" +
            "3\n00:01:05,000 --> 00:01:06,000\n<i></i>\n\n");

        Assert.HasCount(2, cues);
        Assert.AreEqual(TimeSpan.FromSeconds(1), cues[0].Start);
        Assert.AreEqual(TimeSpan.FromSeconds(3.5), cues[0].End);
        Assert.AreEqual("[DOOR OPENS]\nHello.", cues[0].Text);
        Assert.AreEqual(TimeSpan.FromSeconds(62.25), cues[1].Start);
        Assert.AreEqual("Up here", cues[1].Text);
    }

    [TestMethod]
    public async Task ReadsPictureCaptionsFromAnMkv()
    {
        await RequireFfmpeg();
        var mkv = Path.Combine(_folder, "pgs.mkv");
        await Ffmpeg("-i", Sup, "-map", "0", "-c", "copy", mkv);

        var sample = await TrackSampler.SubtitlesAsync(mkv, 0, "Pgs", 10_000, Path.Combine(_folder, "work"), CancellationToken.None);

        Assert.HasCount(PgsReader.Read(Sup).Count, sample.Pictures);
        Assert.IsEmpty(sample.Texts);
        // ffmpeg's MKV muxer moves the first caption to 0; the gaps between captions stay.
        var sup = PgsReader.Read(Sup);
        Assert.AreEqual(sup[2].Start - sup[1].Start, sample.Pictures[2].Start - sample.Pictures[1].Start);
        Assert.IsTrue(Png.Rgba(sample.Pictures[0].Render()).Length > 0);
    }

    [TestMethod]
    public async Task ReadsOnlyTheSlicesOfALongTextTrack()
    {
        await RequireFfmpeg();
        // A caption every 10 seconds for 20 minutes.
        var srt = Path.Combine(_folder, "long.srt");
        var cues = Enumerable.Range(0, 120).Select(i => new SrtCue(TimeSpan.FromSeconds(i * 10), TimeSpan.FromSeconds(i * 10 + 2), $"Line {i}"));
        await File.WriteAllTextAsync(srt, Srt.Write(cues), Encoding.UTF8);
        var mkv = Path.Combine(_folder, "long.mkv");
        await Ffmpeg("-i", srt, "-c:s", "srt", mkv);

        var duration = (long)TimeSpan.FromMinutes(20).TotalMilliseconds;
        var sample = await TrackSampler.SubtitlesAsync(mkv, 0, "Srt", duration, Path.Combine(_folder, "work"), CancellationToken.None);

        Assert.IsTrue(sample.Texts.Count > 0, "found captions");
        Assert.IsTrue(sample.Texts.Count < 120, $"read {sample.Texts.Count} of 120: only the slices");
        CollectionAssert.AreEqual(sample.Texts.OrderBy(c => c.Start).ToList(), sample.Texts);
        // The film's own times are kept: each caption sits near a slice.
        var starts = TrackSampler.SliceStarts(duration);
        foreach (var cue in sample.Texts)
        {
            Assert.IsTrue(starts.Any(s => cue.Start >= s - TimeSpan.FromSeconds(30) && cue.Start <= s + TrackSampler.SliceLength),
                $"{cue.Text} at {cue.Start} is outside every slice");
        }

        var read = await TrackSampler.ReadAsync(sample, "eng", "tesseract", _folder, CancellationToken.None);
        Assert.HasCount(sample.Texts.Count, read);
        Assert.AreEqual(sample.Texts[0].Text, read[0].Text);
    }

    [TestMethod]
    public async Task ListenGivesTwentySecondsOfAac()
    {
        await RequireFfmpeg();
        var mkv = Path.Combine(_folder, "tone.mkv");
        await Ffmpeg("-f", "lavfi", "-i", "sine=frequency=440:sample_rate=48000:duration=40", "-ac", "6", "-c:a", "ac3", mkv);

        var bytes = await TrackSampler.ListenAsync(mkv, 0, 40_000, _folder, TimeSpan.FromSeconds(5), CancellationToken.None);

        Assert.IsTrue(bytes.Length > 1000);
        CollectionAssert.AreEqual("ftyp"u8.ToArray(), bytes.Skip(4).Take(4).ToArray(), "an MP4 (m4a) file");
        Assert.IsEmpty(Directory.GetFiles(_folder, "listen-*"), "temp file removed");
    }

    [TestMethod]
    public async Task ABadTrackSaysWhatFfmpegSaid()
    {
        await RequireFfmpeg();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            TrackSampler.SubtitlesAsync(Mkv, 42, "Srt", 1000, _folder, CancellationToken.None));
        StringAssert.Contains(error.Message, "ffmpeg could not read the track");
    }

    private static async Task RequireFfmpeg()
    {
        try
        {
            var result = await ProcessExecutor.ExecuteProcessAsync("ffmpeg", timeout: TimeSpan.FromSeconds(10), argumentList: ["-version"]);
            if (result.Success)
            {
                return;
            }
        }
        catch (System.ComponentModel.Win32Exception)
        {
        }

        Assert.Inconclusive("ffmpeg is not on PATH.");
    }

    private static async Task Ffmpeg(params string[] arguments)
    {
        var result = await ProcessExecutor.ExecuteProcessAsync("ffmpeg", timeout: TimeSpan.FromMinutes(1),
            argumentList: ["-v", "error", "-nostdin", "-y", .. arguments]);
        Assert.IsTrue(result.Success, result.Error);
    }
}
