using Muxarr.Core.Extensions;
using Muxarr.Core.Language;
using Muxarr.Core.Models;
using Muxarr.Data.Entities;
using Muxarr.Data.Extensions;
using Muxarr.Web.Components.Shared.Modals;
using static Muxarr.Tests.TestData;

namespace Muxarr.Tests;

/// <summary>
/// The original track name backup: which entry a file owns, that names are only
/// ever taken once, and how today's tracks pair with the ones the file arrived with.
/// </summary>
[TestClass]
public class TrackNameBackupTests
{
    private static readonly DateTime Now = new(2026, 11, 2, 12, 0, 0, DateTimeKind.Utc);

    private string _dir = null!;

    [TestInitialize]
    public void Setup()
    {
        _dir = Path.Combine(Path.GetTempPath(), "muxarr-backup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [TestCleanup]
    public void Cleanup()
    {
        Directory.Delete(_dir, true);
    }

    private string MakeMedia(string name, long size = 1000)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllBytes(path, new byte[size]);
        return path;
    }

    private static List<TrackSnapshot> Release(string audioName, string subName)
    {
        return
        [
            Video(0, "Some.Release.2160p"),
            Audio(1, codec: nameof(AudioCodec.TrueHd), channels: 8, trackName: audioName),
            Sub(2, codec: nameof(SubtitleCodec.Pgs), trackName: subName)
        ];
    }

    [TestMethod]
    public void Sync_CapturesOnce_AndWritesTheSidecar()
    {
        var path = MakeMedia("Movie (2020).mkv");

        var first = TrackNameBackups.Sync(path, 1000, 7_200_000, Release("Surround 7.1 Atmos", "Full"), null, null,
            Now);
        // Muxarr renamed the tracks and remuxed the file; the backup keeps the first names.
        var second = TrackNameBackups.Sync(path, 900, 7_199_000, Release("English - TrueHD 7.1", "English"), null,
            first, Now.AddDays(1));

        Assert.AreEqual("Surround 7.1 Atmos", second.Tracks.Single(t => t.Index == 1).Name);
        Assert.AreEqual(900, second.Size);
        Assert.IsTrue(File.Exists(Path.Combine(_dir, TrackNameBackups.SidecarName)));
        Assert.AreEqual(1, TrackNameBackups.Load(_dir)!.Files.Count);
    }

    [TestMethod]
    public void Sync_FollowsAnArrRename()
    {
        var oldPath = MakeMedia("Movie.2020.mkv");
        TrackNameBackups.Sync(oldPath, 1000, 7_200_000, Release("Director's mix", "Full"), null, null, Now);

        var newPath = Path.Combine(_dir, "Movie (2020) [Remux-2160p].mkv");
        File.Move(oldPath, newPath);

        // A fresh row in the database, so only the sidecar knows the names.
        var backup = TrackNameBackups.Sync(newPath, 1000, 7_200_000, Release("English - TrueHD 7.1", "English"),
            null, null, Now.AddHours(1));

        Assert.AreEqual("Director's mix", backup.Tracks.Single(t => t.Index == 1).Name);
        Assert.AreEqual("Movie (2020) [Remux-2160p].mkv", TrackNameBackups.Load(_dir)!.Files.Single().FileName);
    }

    [TestMethod]
    public void Sync_NewReleaseStartsFresh()
    {
        var oldPath = MakeMedia("Movie.2020.WEB.mkv");
        TrackNameBackups.Sync(oldPath, 1000, 7_200_000, Release("Old release", "Old"), null, null, Now);
        File.Delete(oldPath);

        var newPath = MakeMedia("Movie.2020.Remux.mkv", 5000);
        var backup = TrackNameBackups.Sync(newPath, 5000, 7_200_000, Release("New release", "New"), null, null,
            Now.AddDays(1));

        Assert.AreEqual("New release", backup.Tracks.Single(t => t.Index == 1).Name);
        Assert.AreEqual(2, TrackNameBackups.Load(_dir)!.Files.Count, "the old entry waits out its grace period");
    }

    [TestMethod]
    public void Sync_SameNameButDifferentLength_IsANewFile()
    {
        var path = MakeMedia("Episode.mkv");
        var old = TrackNameBackups.Sync(path, 1000, 1_200_000, Release("Old", "Old"), null, null, Now);

        var backup = TrackNameBackups.Sync(path, 2000, 2_400_000, Release("New", "New"), null, old, Now.AddDays(1));

        Assert.AreEqual("New", backup.Tracks.Single(t => t.Index == 1).Name);
        Assert.AreEqual(1, TrackNameBackups.Load(_dir)!.Files.Count, "the old entry is replaced, not kept twice");
    }

    [TestMethod]
    public void Sync_UsesTheFileFromBeforeItsFirstConversion()
    {
        var path = MakeMedia("Movie (2020).mkv");

        var backup = TrackNameBackups.Sync(path, 1000, 7_200_000, Release("English - TrueHD 7.1", "English"),
            Release("Surround 7.1 Atmos", "Full"), null, Now);

        Assert.AreEqual("Surround 7.1 Atmos", backup.Tracks.Single(t => t.Index == 1).Name);
    }

    [TestMethod]
    public void Sync_LeavesAnUnreadableSidecarAlone()
    {
        var sidecar = Path.Combine(_dir, TrackNameBackups.SidecarName);
        File.WriteAllText(sidecar, "{ not json");
        var path = MakeMedia("Movie (2020).mkv");

        var backup = TrackNameBackups.Sync(path, 1000, 7_200_000, Release("Surround", "Full"), null, null, Now);

        Assert.AreEqual("Surround", backup.Tracks.Single(t => t.Index == 1).Name, "the database copy still works");
        Assert.AreEqual("{ not json", File.ReadAllText(sidecar));
    }

    [TestMethod]
    public void Prune_DropsOnlyLongGoneEntries()
    {
        var sidecar = new TrackNameSidecar
        {
            Files =
            [
                new TrackNameBackup { FileName = "here.mkv", LastSeen = Now.AddDays(-90) },
                new TrackNameBackup { FileName = "gone-recently.mkv", LastSeen = Now.AddDays(-3) },
                new TrackNameBackup { FileName = "gone-long-ago.mkv", LastSeen = Now.AddDays(-31) }
            ]
        };

        TrackNameBackups.Prune(sidecar, name => name == "here.mkv", Now);

        CollectionAssert.AreEquivalent(new[] { "here.mkv", "gone-recently.mkv" },
            sidecar.Files.Select(f => f.FileName).ToArray());
    }

    [TestMethod]
    public void NeedsSync_OnlyWhenSomethingMoved()
    {
        var stored = new TrackNameBackup { FileName = "a.mkv", Size = 10, LastSeen = Now };

        Assert.IsFalse(TrackNameBackups.NeedsSync(stored, "/m/a.mkv", 10, Now.AddDays(1)));
        Assert.IsTrue(TrackNameBackups.NeedsSync(stored, "/m/a.mkv", 11, Now.AddDays(1)));
        Assert.IsTrue(TrackNameBackups.NeedsSync(stored, "/m/b.mkv", 10, Now.AddDays(1)));
        Assert.IsTrue(TrackNameBackups.NeedsSync(stored, "/m/a.mkv", 10, Now.AddDays(8)));
        Assert.IsTrue(TrackNameBackups.NeedsSync(null, "/m/a.mkv", 10, Now));
    }

    // Muxarr dropped a French track, moved the SDH subtitle down and read the SDH
    // flag off its name. Every kept track still finds the name it arrived with.
    [TestMethod]
    public void MatchTracks_FollowsRemovalReorderAndNewFlags()
    {
        var original = TrackNameBackups.Capture(
        [
            Video(0, "Video"),
            Audio(1, "French", trackName: "VFF"),
            Audio(2, "English", nameof(AudioCodec.TrueHd), 8, trackName: "Atmos mix"),
            Sub(3, codec: nameof(SubtitleCodec.Pgs), trackName: "English SDH"),
            Sub(4, codec: nameof(SubtitleCodec.Pgs), trackName: "English")
        ], Now);

        var now = new List<TrackSnapshot>
        {
            Video(0),
            Audio(1, "English", nameof(AudioCodec.TrueHd), 8),
            Sub(2, codec: nameof(SubtitleCodec.Pgs)),
            Sub(3, codec: nameof(SubtitleCodec.Pgs), hi: true)
        };

        var matches = TrackNameBackups.MatchTracks(now, original.Tracks);

        Assert.AreEqual("Atmos mix", matches[1].Name);
        Assert.AreEqual("English", matches[2].Name);
        Assert.AreEqual("English SDH", matches[3].Name);
        Assert.AreEqual("Video", matches[0].Name);
    }

    [TestMethod]
    public void MatchTracks_UndeterminedLanguageResolvedLater()
    {
        var original = TrackNameBackups.Capture([Audio(1, "Undetermined", trackName: "Stereo")], Now);

        var matches = TrackNameBackups.MatchTracks([Audio(1, "French")], original.Tracks);

        Assert.AreEqual("Stereo", matches[1].Name);
    }

    [TestMethod]
    public void Template_OriginalNameToken()
    {
        var track = Audio(1, trackName: "English - TrueHD 7.1");
        track.OriginalName = "Surround 7.1";

        Assert.AreEqual("English (Surround 7.1)", track.ApplyTrackNameTemplate("{language} ({originalname})"));
        Assert.AreEqual("English ()", Audio(1).ApplyTrackNameTemplate("{language} ({originalname})"));
    }

    [TestMethod]
    public void Profile_OriginalNameTokenReadsTheBackup()
    {
        var file = MakeFile("English", Video(0), Audio(1, trackName: "English"));
        file.OriginalTrackNames = TrackNameBackups.Capture([Video(0), Audio(1, trackName: "Theatrical mix")], Now);
        var profile = MakeProfile(audio: new TrackSettings
        {
            Enabled = true,
            AllowedLanguages = [new LanguagePreference(IsoLanguage.Find("English"))],
            StandardizeTrackNames = true,
            TrackNameTemplate = "{language} - {originalname}"
        });

        var target = file.BuildTargetFromProfile(profile);

        Assert.AreEqual("English - Commentary-free mix", target.Tracks.Single(t => t.Index == 1).Name);
    }

    [TestMethod]
    public void CustomConversion_UseOriginalName()
    {
        var file = MakeFile("English", Video(0), Audio(1, trackName: "English"), Audio(2, "French", trackName: "French"));
        file.OriginalTrackNames = TrackNameBackups.Capture(
            [Video(0), Audio(1, trackName: "Surround 5.1"), Audio(2, "French", trackName: "French")], Now);
        var editor = new CustomConversionEditor(file);
        var english = editor.Tracks.Single(t => t.Index == 1);
        var french = editor.Tracks.Single(t => t.Index == 2);

        Assert.IsNull(editor.OriginalNameOf(french), "nothing to offer when the name never changed");
        editor.UseOriginalName(english);

        Assert.AreEqual("Surround 5.1", english.Name);
        Assert.AreEqual("Surround 5.1", editor.Build().Tracks.Single(t => t.Index == 1).Name);
    }
}
