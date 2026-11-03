using Muxarr.Core.Models;
using Muxarr.Core.Utilities;
using Muxarr.Data.Entities;
using Muxarr.Data.Extensions;

namespace Muxarr.Tests;

/// <summary>
/// Global (file-level) tags: read from ffprobe, listed on the file page and kept
/// in the track name backup.
/// </summary>
[TestClass]
public class GlobalTagTests
{
    private static readonly DateTime Now = new(2026, 11, 3, 12, 0, 0, DateTimeKind.Utc);

    private static List<FileTag> Tags(params (string Name, string Value)[] tags)
    {
        return tags.Select(t => new FileTag { Name = t.Name, Value = t.Value }).ToList();
    }

    [TestMethod]
    public void FromFormatTags_KeepsTagsInOrder_AndSkipsContainerFields()
    {
        var format = new Dictionary<string, string>
        {
            ["title"] = "Movie (2020)",
            ["TMDB"] = "movie/603",
            ["encoder"] = "libebml v1.4.5 + libmatroska v1.7.1",
            ["creation_time"] = "2026-11-03T00:00:00.000000Z",
            ["RELEASE_NAME"] = "Movie.2020.2160p.UHD.BluRay.REMUX"
        };

        var tags = FileTag.FromFormatTags(format);

        CollectionAssert.AreEqual(new[] { "TMDB", "RELEASE_NAME" }, tags.Select(t => t.Name).ToArray());
        Assert.AreEqual("movie/603", tags[0].Value);
        Assert.AreEqual(0, FileTag.FromFormatTags(null).Count);
    }

    [TestMethod]
    public void TagsChanged_OnlyWhenTheFileHasDifferentTags()
    {
        var stored = Tags(("TMDB", "603"));

        Assert.IsFalse(TrackNameBackups.TagsChanged(stored, Tags(("TMDB", "603"))));
        Assert.IsFalse(TrackNameBackups.TagsChanged(stored, []), "a file without tags never clears the backup");
        Assert.IsFalse(TrackNameBackups.TagsChanged(stored, null));
        Assert.IsTrue(TrackNameBackups.TagsChanged(stored, Tags(("TMDB", "604"))));
        Assert.IsTrue(TrackNameBackups.TagsChanged(stored, Tags(("TMDB", "603"), ("IMDB", "tt0133093"))));
        Assert.IsTrue(TrackNameBackups.TagsChanged([], Tags(("TMDB", "603"))));
    }

    [TestMethod]
    public void Backup_KeepsTheLatestTags_AndSurvivesAFileThatLostThem()
    {
        var dir = Path.Combine(Path.GetTempPath(), "muxarr-tags-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "Movie (2020).mkv");
            File.WriteAllBytes(path, new byte[1000]);
            List<TrackSnapshot> tracks = [TestData.Video(0, "Video")];

            var first = TrackNameBackups.Sync(path, 1000, 7_200_000, tracks, null, null, Now,
                currentTags: Tags(("TMDB", "603")));
            Assert.AreEqual("603", first.GlobalTags.Single().Value);

            // A retag adds one.
            Assert.IsTrue(TrackNameBackups.NeedsSync(first, path, 1000, Now, currentTags: Tags(("TMDB", "603"), ("IMDB", "tt0133093"))));
            var retagged = TrackNameBackups.Sync(path, 1000, 7_200_000, tracks, null, first, Now,
                currentTags: Tags(("TMDB", "603"), ("IMDB", "tt0133093")));
            Assert.AreEqual(2, retagged.GlobalTags.Count);

            // A remux that dropped them keeps the backed-up set.
            var lost = TrackNameBackups.Sync(path, 900, 7_200_000, tracks, null, retagged, Now.AddDays(1),
                currentTags: []);
            Assert.AreEqual(2, lost.GlobalTags.Count);
            Assert.AreEqual(2, TrackNameBackups.Load(dir)!.Files.Single().GlobalTags.Count, "the sidecar has them too");
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    // The scan reads them through ffprobe, so a real MKV with mkvpropedit-written tags proves the path.
    [TestMethod]
    public async Task SetFileDataFromFFprobe_ReadsMkvGlobalTags()
    {
        var work = Path.Combine(Path.GetTempPath(), $"muxarr_tags_{Guid.NewGuid():N}.mkv");
        var xml = work + ".xml";
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "test.mkv"), work, true);
        File.WriteAllText(xml, """
            <?xml version="1.0" encoding="UTF-8"?>
            <Tags>
              <Tag>
                <Targets><TargetTypeValue>50</TargetTypeValue></Targets>
                <Simple><Name>TMDB</Name><String>movie/603</String></Simple>
                <Simple><Name>RELEASE_GROUP</Name><String>FraMeSToR</String></Simple>
              </Tag>
            </Tags>
            """);
        try
        {
            var edit = await ProcessExecutor.ExecuteProcessAsync("mkvpropedit", $"\"{work}\" --tags global:\"{xml}\"",
                TimeSpan.FromMinutes(1));
            Assert.AreEqual(0, edit.ExitCode, edit.Error);

            var file = new MediaFile { Path = work };
            await file.SetFileDataFromFFprobe();

            var tags = file.Snapshot.GlobalTags.ToDictionary(t => t.Name, t => t.Value);
            Assert.AreEqual("movie/603", tags["TMDB"]);
            Assert.AreEqual("FraMeSToR", tags["RELEASE_GROUP"]);
            Assert.IsFalse(tags.Keys.Any(k => k.Equals("title", StringComparison.OrdinalIgnoreCase)));
            Assert.IsFalse(tags.Keys.Any(k => k.Equals("encoder", StringComparison.OrdinalIgnoreCase)));
        }
        finally
        {
            foreach (var p in new[] { work, xml })
            {
                if (File.Exists(p))
                {
                    File.Delete(p);
                }
            }
        }
    }
}
