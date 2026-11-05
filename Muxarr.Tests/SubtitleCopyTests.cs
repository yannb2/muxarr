using System.Text;
using Muxarr.Core.Extensions;
using Muxarr.Core.Language;
using Muxarr.Core.MkvToolNix;
using Muxarr.Core.Models;
using Muxarr.Core.SubtitleCopy;
using Muxarr.Data.Entities;
using Muxarr.Data.Extensions;
using Muxarr.Web.Services;
using Muxarr.Web.Services.SubtitleCopy;
using static Muxarr.Tests.TestData;

namespace Muxarr.Tests;

// Fork: the Copy subtitles page.
[TestClass]
public class SubtitleCopyTests
{
    [TestMethod]
    [DataRow("Show (2020) - S01E02 - Pilot [Bluray-1080p Remux].mkv", 1, new[] { 2 }, null)]
    [DataRow("show.s01e02.1080p.bluray.remux.mkv", 1, new[] { 2 }, null)]
    [DataRow("Show - S02E03E04 - Two Parter.mkv", 2, new[] { 3, 4 }, null)]
    [DataRow("Show - S02E03-E05.mkv", 2, new[] { 3, 4, 5 }, null)]
    [DataRow("Show.1x07.HDTV.srt", 1, new[] { 7 }, null)]
    [DataRow("Show - S01E12 - 012 - Title [Bluray-1080p].mkv", 1, new[] { 12 }, 12)]
    [DataRow("Show S00E01 Special.fr.srt", 0, new[] { 1 }, null)]
    public void EpisodeKey_ReadsSeasonAndEpisodes(string name, int season, int[] episodes, int? absolute)
    {
        var key = EpisodeKey.Parse("/x/" + name)!;

        Assert.AreEqual(season, key.Season);
        CollectionAssert.AreEqual(episodes, key.Episodes.ToArray());
        Assert.AreEqual(absolute, key.Absolute);
    }

    [TestMethod]
    public void EpisodeKey_AbsoluteOnlyForFansubNames()
    {
        var key = EpisodeKey.Parse("/x/[Group] Show - 012 [1080p].mkv")!;

        Assert.IsFalse(key.HasSeasonEpisode);
        Assert.AreEqual(12, key.Absolute);
        Assert.AreEqual("#012", key.Label);
        Assert.IsTrue(key.SameEpisode(EpisodeKey.Parse("/x/Show - S01E12 - 012 - Title.mkv")!));
        Assert.IsNull(EpisodeKey.Parse("/x/Show (2020).mkv"));
    }

    [TestMethod]
    public void EpisodeKey_DifferentEpisodesDoNotMatch()
    {
        var one = EpisodeKey.Parse("/a/Show S01E02.mkv")!;

        Assert.IsTrue(one.SameEpisode(EpisodeKey.Parse("/b/show.s01e02.fr.srt")!));
        Assert.IsFalse(one.SameEpisode(EpisodeKey.Parse("/b/show.s01e03.fr.srt")!));
        Assert.IsFalse(one.SameEpisode(EpisodeKey.Parse("/b/show.s02e02.fr.srt")!));
        Assert.IsFalse(one.SameEpisode(EpisodeKey.Parse("/b/show.s01e02e03.mkv")!), "a double episode is another file");
    }

    [TestMethod]
    [DataRow("Show S01E02.fr.srt", "fre", false, false)]
    [DataRow("Show S01E02.fr.forced.srt", "fre", true, false)]
    [DataRow("Show S01E02.en.sdh.srt", "eng", false, true)]
    [DataRow("Show S01E02.English.HI.srt", "eng", false, true)]
    [DataRow("Show.S01E02.1080p.WEB.French.srt", "fre", false, false)]
    [DataRow("Show S01E02.srt", null, false, false)]
    public void ParseName_ReadsLanguageAndFlags(string name, string? language, bool forced, bool sdh)
    {
        var info = SubtitleFiles.ParseName("/x/" + name);

        Assert.AreEqual(language, info.LanguageCode is { } code ? IsoLanguage.Find(code).ThreeLetterCode : null);
        Assert.AreEqual(forced, info.IsForced);
        Assert.AreEqual(sdh, info.IsHearingImpaired);
    }

    [TestMethod]
    public void CharsetFor_TellsUtf8FromWindows1252()
    {
        Assert.AreEqual("UTF-8", SubtitleFiles.CharsetFor(Encoding.UTF8.GetBytes("Déjà vu")));
        Assert.IsNull(SubtitleFiles.CharsetFor([0xEF, 0xBB, 0xBF, 0x41]), "a byte order mark speaks for itself");
        Assert.AreEqual("windows-1252", SubtitleFiles.CharsetFor([0x44, 0xE9, 0x6A, 0xE0, 0x20, 0x93, 0x76, 0x75, 0x94]));
    }

    [TestMethod]
    public void StretchFor_KnowsPalAndLeavesSmallDifferencesAlone()
    {
        var film = 2_640_000L; // 44 min at 23.976
        var pal = (long)(film * 24000.0 / 25025);

        Assert.AreSame(SubtitleTiming.PalToFilm, SubtitleTiming.StretchFor(film, pal));
        Assert.AreSame(SubtitleTiming.FilmToPal, SubtitleTiming.StretchFor(pal, film));
        Assert.AreSame(SubtitleTiming.None, SubtitleTiming.StretchFor(film, film + 4_000), "a longer logo is a shift");
        Assert.AreSame(SubtitleTiming.None, SubtitleTiming.StretchFor(film, film - 300_000), "another cut is no speed change");
        Assert.AreEqual((long)Math.Round(1000 * 25025 / 24000.0), SubtitleTiming.Apply(1000, SubtitleTiming.PalToFilm, 0));
    }

    private static List<long> Captions(int count, int seed)
    {
        var random = new Random(seed);
        var starts = new List<long>();
        var at = 5_000L;
        for (var i = 0; i < count; i++)
        {
            at += random.Next(1_200, 6_000);
            starts.Add(at);
        }

        return starts;
    }

    [TestMethod]
    public void FindOffset_FindsTheShiftOfTheSameTrack()
    {
        var mine = Captions(300, 1);
        var theirs = mine.Select(ms => ms - 1_234 + (ms % 3) * 10).ToList();

        var result = SubtitleTiming.FindOffset(mine, theirs);

        Assert.AreEqual(SubtitleTiming.Quality.Good, result.Quality);
        Assert.IsTrue(Math.Abs(result.OffsetMs - 1_244) <= 20, $"offset {result.OffsetMs}");
        Assert.IsTrue(result.Share > 0.9);
    }

    [TestMethod]
    public void FindOffset_AnotherLanguageStillLinesUp()
    {
        // Another language: two thirds of the captions start at the same time, the rest elsewhere.
        var mine = Captions(300, 2);
        var random = new Random(3);
        var theirs = mine.Where((_, i) => i % 3 != 0).Select(ms => ms + 2_500 + random.Next(-60, 60))
            .Concat(Captions(60, 4).Select(ms => ms + 700))
            .ToList();

        var result = SubtitleTiming.FindOffset(mine, theirs);

        Assert.AreNotEqual(SubtitleTiming.Quality.NoMatch, result.Quality);
        Assert.IsTrue(Math.Abs(result.OffsetMs - -2_500) <= 60, $"offset {result.OffsetMs}");
    }

    [TestMethod]
    public void FindOffset_UnrelatedCaptionsDoNotMatch()
    {
        var result = SubtitleTiming.FindOffset(Captions(200, 5), Captions(200, 6).Select(ms => ms * 3 / 2).ToList());

        Assert.AreNotEqual(SubtitleTiming.Quality.Good, result.Quality);
        Assert.AreEqual(SubtitleTiming.Quality.NoMatch, SubtitleTiming.FindOffset([], [1, 2, 3]).Quality);
    }

    [TestMethod]
    public void FindOffset_AfterThePalStretch()
    {
        var mine = Captions(400, 7);
        var pal = mine.Select(ms => (long)Math.Round((ms - 800) * 24000.0 / 25025)).ToList();

        var stretched = pal.Select(ms => SubtitleTiming.Apply(ms, SubtitleTiming.PalToFilm, 0)).ToList();
        var result = SubtitleTiming.FindOffset(mine, stretched);

        Assert.AreEqual(SubtitleTiming.Quality.Good, result.Quality);
        Assert.IsTrue(Math.Abs(result.OffsetMs - 800) <= 5, $"offset {result.OffsetMs}");
        Assert.AreNotEqual(SubtitleTiming.Quality.Good, SubtitleTiming.FindOffset(mine, pal).Quality, "unstretched, the drift breaks it");
    }

    [TestMethod]
    public void SyncValue_IsMkvmergesSyntax()
    {
        Assert.IsNull(new DonorTrack().SyncValue());
        Assert.AreEqual("-1500", new DonorTrack { OffsetMs = -1500 }.SyncValue());
        Assert.AreEqual("200,25025/24000", new DonorTrack { OffsetMs = 200, StretchNumerator = 25025, StretchDenominator = 24000 }.SyncValue());
    }

    private static Profile FrenchEnglishProfile(bool standardize = true) => MakeProfile(
        subtitle: new TrackSettings
        {
            Enabled = true,
            AllowedLanguages = [new LanguagePreference(IsoLanguage.Find("English")), new LanguagePreference(IsoLanguage.Find("French"))],
            StandardizeTrackNames = standardize,
            TrackNameTemplate = standardize ? "{language} - {codec}" : ""
        });

    private static MediaFile Mkv(params TrackSnapshot[] tracks)
    {
        var file = MakeFile("English", tracks);
        file.Path = "/tv/Show/Season 01/Show S01E02.mkv";
        file.Snapshot.ContainerType = "Matroska";
        return file;
    }

    [TestMethod]
    public void BuildTargetWithDonors_RunsTheDonorThroughTheProfile()
    {
        var file = Mkv(Video(0), Audio(1), Sub(2, codec: nameof(SubtitleCodec.Pgs)));
        var french = new DonorTrack { Path = "/downloads/other.mkv", TrackId = 5, OffsetMs = 1200 };
        var german = new DonorTrack { Path = "/downloads/other.mkv", TrackId = 6 };

        var plan = file.BuildTargetWithDonors(FrenchEnglishProfile(),
        [
            (french, Sub(5, "French", nameof(SubtitleCodec.Pgs), trackName: "Français")),
            (german, Sub(6, "German", nameof(SubtitleCodec.Pgs)))
        ]);

        CollectionAssert.AreEqual(new[] { 0, 1, 2 }, plan.Tracks.Select(t => t.Index).ToArray(), "the file's own tracks stay as they are");
        Assert.AreEqual(1, plan.Donors!.Count, "German is not an allowed language");
        Assert.AreSame(french, plan.Donors[0]);
        Assert.AreEqual("French - PGS", french.Output!.Name);
        Assert.AreEqual("fre", french.Output.LanguageCode);
        Assert.IsNull(german.Output);

        var output = plan.OutputTracks();
        Assert.AreEqual(4, output.Count);
        Assert.AreEqual(MediaTrackType.Subtitles, output[3].Type);
        CollectionAssert.AreEqual(new[] { "0:0", "0:1", "0:2", "1:5" }, plan.TrackOrder());
    }

    [TestMethod]
    public void BuildTargetWithDonors_PageOverridesWin()
    {
        var file = Mkv(Video(0), Audio(1));
        var srt = new DonorTrack { Path = "/downloads/Show S01E02.srt", TrackId = 0, LanguageCode = "fre", IsForced = true };

        var plan = file.BuildTargetWithDonors(FrenchEnglishProfile(),
            [(srt, Sub(0, "Unknown", nameof(SubtitleCodec.Srt), languageCode: "und"))]);

        Assert.AreEqual("fre", srt.Output!.LanguageCode);
        Assert.IsTrue(srt.Output.IsForced);
        Assert.AreEqual(1, plan.Donors!.Count);
    }

    [TestMethod]
    public void TrackOrder_NumbersDonorsAfterTheMadeSrts()
    {
        var plan = new ConversionPlan
        {
            Tracks =
            [
                new TrackPlan { Index = 0, Type = MediaTrackType.Video },
                new TrackPlan { Index = 2, Type = MediaTrackType.Subtitles }
            ],
            AddedSubtitles = [new AddedSubtitle("/tmp/a.srt", "eng", "English", 2)],
            Donors =
            [
                new DonorTrack { Path = "/d/x.mkv", TrackId = 4, Output = new TrackPlan { Name = "French", LanguageCode = "fre" }, BeforeIndex = 0 },
                new DonorTrack { Path = "/d/y.srt", Output = null },
                new DonorTrack { Path = "/d/z.srt", TrackId = 0, Output = new TrackPlan { Name = "Dutch", LanguageCode = "dut" } }
            ]
        };

        CollectionAssert.AreEqual(new[] { "2:4", "0:0", "1:0", "0:2", "3:0" }, plan.TrackOrder());
        CollectionAssert.AreEqual(new[] { -2, 0, -1, 2, -3 }, plan.OutputTracks().Select(t => t.Index).ToArray());
    }

    [TestMethod]
    public void Planner_DonorsTakeARemux()
    {
        var file = Mkv(Video(0), Audio(1));
        var target = file.ToTargetSnapshotFromSource();
        Assert.AreEqual(ConversionPlanner.ConversionStrategy.Skip, ConversionPlanner.Plan(file.Snapshot, target).Strategy);

        target.Donors = [new DonorTrack { Path = "/d/x.srt", Output = new TrackPlan { Name = "French", LanguageCode = "fre" } }];
        var result = ConversionPlanner.Plan(file.Snapshot, target);

        Assert.AreEqual(ConversionPlanner.ConversionStrategy.Remux, result.Strategy);
        Assert.AreEqual(1, result.Delta.Donors!.Count);
    }

    [TestMethod]
    public async Task Remux_TakesTrackFromDonorMkvAndShiftsALooseSrt()
    {
        var folder = Path.Combine(Path.GetTempPath(), "muxarr-copy-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            // Windows-1252, no byte order mark: an older French SRT.
            var srt = Path.Combine(folder, "Show S01E02.fr.srt");
            var text = "1\r\n00:00:00,100 --> 00:00:00,600\r\nDéjà\r\n\r\n";
            await File.WriteAllBytesAsync(srt, text.Select(c => c == 'é' ? (byte)0xE9 : c == 'à' ? (byte)0xE0 : (byte)c).ToArray());
            var donorMkv = Path.GetFullPath(Path.Combine("Fixtures", "test.mkv"));
            var output = Path.Combine(folder, "out.mkv");
            var plan = new ConversionPlan
            {
                Tracks =
                [
                    new TrackPlan { Index = 0, Type = MediaTrackType.Video },
                    new TrackPlan { Index = 1, Type = MediaTrackType.Audio },
                    new TrackPlan { Index = 3, Type = MediaTrackType.Subtitles }
                ],
                Donors =
                [
                    new DonorTrack
                    {
                        Path = donorMkv, TrackId = 4, BeforeIndex = 3,
                        Output = new TrackPlan { Name = "Dutch - SRT", LanguageCode = "dut", IsForced = false, IsDefault = false }
                    },
                    new DonorTrack
                    {
                        Path = srt, TrackId = 0, OffsetMs = 250,
                        Output = new TrackPlan { Name = "French - SRT", LanguageCode = "fre", IsForced = true, IsDefault = true }
                    }
                ]
            };

            var result = await MkvMerge.Remux(Path.Combine("Fixtures", "test.mkv"), output, plan);
            Assert.IsTrue(MkvMerge.IsSuccess(result), result.Error + result.Output);

            var tracks = (await MkvMerge.GetFileInfo(output)).Result!.Tracks;
            Assert.AreEqual(5, tracks.Count);
            Assert.AreEqual("Dutch - SRT", tracks[2].Properties.TrackName);
            Assert.AreEqual("English SDH", tracks[3].Properties.TrackName);
            Assert.AreEqual("French - SRT", tracks[4].Properties.TrackName);
            Assert.AreEqual("fre", tracks[4].Properties.Language);
            Assert.IsTrue(tracks[4].Properties.ForcedTrack);
            Assert.IsTrue(tracks[4].Properties.DefaultTrack);

            var extracted = Path.Combine(folder, "fr.srt");
            var extract = await Muxarr.Core.Utilities.ProcessExecutor.ExecuteProcessAsync("mkvextract",
                $"\"{output}\" tracks 4:\"{extracted}\"", TimeSpan.FromSeconds(30));
            Assert.AreEqual(0, extract.ExitCode, extract.Error);
            var written = await File.ReadAllTextAsync(extracted);
            StringAssert.Contains(written, "00:00:00,350 --> 00:00:00,850", "shifted by 250 ms");
            StringAssert.Contains(written, "Déjà", "read as Windows-1252, written as UTF-8");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [TestMethod]
    public async Task Engine_ProbesALooseSrtAndReadsItsCueStarts()
    {
        var folder = Path.Combine(Path.GetTempPath(), "muxarr-copy-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var srt = Path.Combine(folder, "Show S01E02.fr.forced.srt");
            await File.WriteAllTextAsync(srt, "1\n00:00:01,000 --> 00:00:02,000\nUn\n\n2\n00:00:05,500 --> 00:00:06,000\nDeux\n\n");

            Assert.AreEqual(1, SubtitleCopyEngine.FindDonorFiles(folder).Count);
            var donor = await SubtitleCopyEngine.ProbeAsync(srt);
            Assert.IsNull(donor.Error, donor.Error);
            Assert.AreEqual(1, donor.Subtitles.Count);
            var kind = SubtitleCopyEngine.KindOf(donor, donor.Subtitles[0]);
            Assert.AreEqual("fre", IsoLanguage.Find(kind.LanguageCode).ThreeLetterCode);
            Assert.IsTrue(kind.Forced);
            Assert.IsFalse(kind.Pictures);

            var starts = await CueReader.StartsAsync(srt, donor.Subtitles[0].Index, donor.Subtitles[0].Codec, null, CancellationToken.None);
            CollectionAssert.AreEqual(new long[] { 1000, 5500 }, starts);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [TestMethod]
    public void Match_GivesEachEpisodeItsDonors()
    {
        var rows = new List<CopyRow>
        {
            new() { Target = Mkv(Video(0)), Key = EpisodeKey.Parse("/tv/Show S01E01.mkv") },
            new() { Target = Mkv(Video(0)), Key = EpisodeKey.Parse("/tv/Show S01E02.mkv") }
        };
        var donors = new List<DonorFile>
        {
            new() { Path = "/d/show.s01e01.mkv", Key = EpisodeKey.Parse("/d/show.s01e01.mkv") },
            new() { Path = "/d/show.s01e01.fr.srt", Key = EpisodeKey.Parse("/d/show.s01e01.fr.srt") },
            new() { Path = "/d/show.s01e03.mkv", Key = EpisodeKey.Parse("/d/show.s01e03.mkv") }
        };

        SubtitleCopyEngine.Match(rows, donors);

        Assert.AreEqual(2, rows[0].Assignments.Count);
        Assert.AreEqual(0, rows[1].Assignments.Count, "no match: the user picks one");
    }
}
