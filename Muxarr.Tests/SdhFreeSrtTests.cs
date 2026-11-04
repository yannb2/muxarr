using Arrkit.Subtitles;
using Muxarr.Core.Extensions;
using Muxarr.Core.Language;
using Muxarr.Core.MkvToolNix;
using Muxarr.Core.Models;
using Muxarr.Data.Entities;
using Muxarr.Data.Extensions;
using Muxarr.Web.Services;
using static Muxarr.Tests.TestData;

namespace Muxarr.Tests;

// Fork: "Add an SRT without SDH" in the subtitle settings.
[TestClass]
public class SdhFreeSrtTests
{
    private static Profile Profile(bool on = true, bool standardize = false) => MakeProfile(
        subtitle: new TrackSettings
        {
            Enabled = true,
            AllowedLanguages = [new LanguagePreference(IsoLanguage.Find("English")), new LanguagePreference(IsoLanguage.Find("French"))],
            AddSrtWithoutSdh = on,
            StandardizeTrackNames = standardize,
            TrackNameTemplate = standardize ? "{language} - {codec}" : ""
        });

    private static MediaFile Mkv(params TrackSnapshot[] tracks)
    {
        var file = MakeFile("English", tracks);
        file.Snapshot.ContainerType = "Matroska";
        return file;
    }

    [TestMethod]
    public void Find_OnlySdhPgs_GivesTheTrackToRead()
    {
        var file = Mkv(Video(0), Audio(1), Sub(2, codec: nameof(SubtitleCodec.Pgs), hi: true));
        var profile = Profile();

        var found = SdhFreeSrt.Find(file, file.BuildTargetFromProfile(profile), profile);

        Assert.AreEqual(1, found.Count);
        Assert.AreEqual(2, found[0].SourceIndex);
        Assert.AreEqual("eng", found[0].LanguageCode);
        Assert.AreEqual("English", found[0].Name, "no naming template: the language's name");
    }

    [TestMethod]
    public void Find_NamesTheSrtWithTheProfileTemplate()
    {
        var file = Mkv(Video(0), Audio(1), Sub(2, codec: nameof(SubtitleCodec.Pgs), hi: true));
        var profile = Profile(standardize: true);

        var found = SdhFreeSrt.Find(file, file.BuildTargetFromProfile(profile), profile);

        Assert.AreEqual("English - SRT", found.Single().Name);
    }

    [TestMethod]
    public void Find_NothingWhenOffNotMkvOrAPlainTrackExists()
    {
        var sdhOnly = Mkv(Video(0), Audio(1), Sub(2, codec: nameof(SubtitleCodec.Pgs), hi: true));
        Assert.AreEqual(0, SdhFreeSrt.Find(sdhOnly, sdhOnly.BuildTargetFromProfile(Profile(false)), Profile(false)).Count);

        var mp4 = Mkv(Video(0), Audio(1), Sub(2, codec: nameof(SubtitleCodec.Pgs), hi: true));
        mp4.Snapshot.ContainerType = "MP4/QuickTime";
        Assert.AreEqual(0, SdhFreeSrt.Find(mp4, mp4.BuildTargetFromProfile(Profile()), Profile()).Count);

        // A plain English track next to the SDH one: nothing to add. Forced French only: not a full track.
        var both = Mkv(Video(0), Audio(1),
            Sub(2, codec: nameof(SubtitleCodec.Pgs), hi: true),
            Sub(3, codec: nameof(SubtitleCodec.Pgs)),
            Sub(4, "French", nameof(SubtitleCodec.Pgs), forced: true));
        Assert.AreEqual(0, SdhFreeSrt.Find(both, both.BuildTargetFromProfile(Profile()), Profile()).Count);
    }

    [TestMethod]
    public void Find_NeedsAPictureTrackToRead()
    {
        var file = Mkv(Video(0), Audio(1), Sub(2, codec: nameof(SubtitleCodec.Srt), hi: true));
        Assert.AreEqual(0, SdhFreeSrt.Find(file, file.BuildTargetFromProfile(Profile()), Profile()).Count);
    }

    [TestMethod]
    public void Cues_DropTheSdhOnlyCaptionsAndKeepTheCleanText()
    {
        var ocr = new List<OcrCaption>
        {
            new(0, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), false, "[door slams]", SdhCleaner.Clean("[door slams]")),
            new(1, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(4), false, "JOHN: Hello.", SdhCleaner.Clean("JOHN: Hello."))
        };

        var cues = SdhFreeSrt.Cues(ocr);

        Assert.AreEqual(1, cues.Count);
        Assert.AreEqual("Hello.", cues[0].Text);
    }

    [TestMethod]
    public void OutputTracks_PutsTheSrtBeforeItsTrack()
    {
        var plan = new ConversionPlan
        {
            Tracks =
            [
                new TrackPlan { Index = 0, Type = MediaTrackType.Video },
                new TrackPlan { Index = 1, Type = MediaTrackType.Audio },
                new TrackPlan { Index = 2, Type = MediaTrackType.Subtitles }
            ],
            AddedSubtitles = [new AddedSubtitle("/tmp/a.srt", "eng", "English", 2), new AddedSubtitle("/tmp/b.srt", "fre", "French", -1)]
        };

        var order = plan.OutputTracks().Select(t => t.Index).ToArray();

        CollectionAssert.AreEqual(new[] { 0, 1, -1, 2, -2 }, order);
    }

    [TestMethod]
    public void Validator_CountsTheAddedSrt()
    {
        var source = Mkv(Video(0), Audio(1), Sub(2, codec: nameof(SubtitleCodec.Pgs), hi: true));
        source.Snapshot.DurationMs = 10_000;
        var target = new ConversionPlan
        {
            Tracks =
            [
                new TrackPlan { Index = 0, Type = MediaTrackType.Video },
                new TrackPlan { Index = 1, Type = MediaTrackType.Audio },
                new TrackPlan { Index = 2, Type = MediaTrackType.Subtitles }
            ],
            AddedSubtitles = [new AddedSubtitle("/tmp/a.srt", "eng", "English", 2)]
        };
        var actual = Mkv(Video(0), Audio(1), Sub(2, trackName: "English"), Sub(3, codec: nameof(SubtitleCodec.Pgs), hi: true));
        actual.Snapshot.DurationMs = 10_000;

        OutputValidator.ValidateOrThrow(actual, source, target);
        Assert.AreEqual(0, OutputValidator.MetadataDifferences(actual, target).Count);

        target.AddedSubtitles = null;
        Assert.ThrowsExactly<Exception>(() => OutputValidator.ValidateOrThrow(actual, source, target));
    }

    [TestMethod]
    public async Task Remux_MuxesTheSrtInFrontOfItsTrack()
    {
        var folder = Path.Combine(Path.GetTempPath(), "muxarr-srt-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var srt = Path.Combine(folder, "track3.srt");
            await File.WriteAllTextAsync(srt, Srt.Write([new SrtCue(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), "Hello.")]));
            var output = Path.Combine(folder, "out.mkv");
            var plan = new ConversionPlan
            {
                Tracks =
                [
                    new TrackPlan { Index = 0, Type = MediaTrackType.Video },
                    new TrackPlan { Index = 1, Type = MediaTrackType.Audio },
                    new TrackPlan { Index = 3, Type = MediaTrackType.Subtitles }
                ],
                AddedSubtitles = [new AddedSubtitle(srt, "eng", "English - SRT", 3)]
            };

            var result = await MkvMerge.Remux(Path.Combine("Fixtures", "test.mkv"), output, plan);
            Assert.IsTrue(MkvMerge.IsSuccess(result), result.Error);

            var info = await MkvMerge.GetFileInfo(output);
            var tracks = info.Result!.Tracks;
            Assert.AreEqual(4, tracks.Count);
            Assert.AreEqual("subtitles", tracks[2].Type);
            Assert.AreEqual("English - SRT", tracks[2].Properties.TrackName);
            Assert.AreEqual("eng", tracks[2].Properties.Language);
            Assert.IsFalse(tracks[2].Properties.DefaultTrack);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
