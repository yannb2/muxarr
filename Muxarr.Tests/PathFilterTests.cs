using Muxarr.Core.Utilities;

namespace Muxarr.Tests;

[TestClass]
public class PathFilterTests
{
    private static string P(string unixPath)
    {
        return unixPath.Replace('/', Path.DirectorySeparatorChar);
    }

    [TestMethod]
    [DataRow("/media/movies/._Movie.mkv")]
    [DataRow("/media/movies/._The.Office.S08E05.720p.mkv")]
    [DataRow("/media/@eaDir/thumb.mkv")]
    [DataRow("/media/#recycle/old.mp4")]
    [DataRow("/media/@Recycle/old.mp4")]
    [DataRow("/movies/live-action/.recycle/Movie (2026)/Movie (2026).mkv")]
    [DataRow("/media/.@__thumb/poster.mp4")]
    [DataRow("/media/$RECYCLE.BIN/file.mkv")]
    [DataRow("/media/System Volume Information/file.mkv")]
    [DataRow("/media/lost+found/file.mkv")]
    [DataRow("/media/.Trash/file.mkv")]
    [DataRow("/media/.AppleDouble/file.mkv")]
    [DataRow("/media/.zfs/snapshot/file.mkv")]
    [DataRow("/media/movies/Movie-trailer.mkv")]
    [DataRow("/media/movies/Movie.trailer.mp4")]
    [DataRow("/media/movies/Movie_TRAILER.mkv")]
    [DataRow("/media/movies/Movie-sample.mkv")]
    [DataRow("/media/movies/Movie.sample.mp4")]
    [DataRow("/media/movies/Movie_SAMPLE.mkv")]
    [DataRow("/media/movies/Movie (2026)/trailer.mkv")]
    [DataRow("/media/movies/Movie (2026)/TRAILER.mp4")]
    [DataRow("/media/movies/Movie (2026)/sample.mkv")]
    [DataRow("/media/movies/Movie (2026)/SAMPLE.mp4")]
    [DataRow("/media/movies/Movie (2026)/Trailers/Teaser Trailer.mp4")]
    [DataRow("/media/movies/Movie (2026)/trailers/trailer.mkv")]
    [DataRow("/media/movies/Movie (2026)/Samples/sample.mkv")]
    [DataRow("/media/tv/Show (2026)/Season 01/Trailers/Show trailer.mkv")]
    [DataRow(@"C:\media\movies\Movie (2026)\Movie-trailer.mkv")]
    public void ShouldIgnore_ReturnsTrue(string path)
    {
        Assert.IsTrue(PathFilter.ShouldIgnore(P(path)));
    }

    [TestMethod]
    [DataRow("/media/movies/Movie.mkv")]
    [DataRow("/media/movies/The.Office.S08E05.720p.mkv")]
    [DataRow("/media/lost and found/Movie.mp4")]
    [DataRow("/media/my@eaDir/Movie.mkv")]
    [DataRow("/media/.Trash report/Movie.mkv")]
    [DataRow("/media/tv/Trailer Park Boys/Season 01/Trailer Park Boys S01E01.mkv")]
    [DataRow("/media/movies/The Trailer.mkv")]
    [DataRow("/media/movies/Movie trailer.webm")]
    [DataRow("/media/movies/Sample People (2026).mkv")]
    [DataRow("/media/movies/Movie sample.webm")]
    [DataRow("/media/movies/Movie-trailer-extended.mkv")]
    [DataRow("/media/movies/Movie-sample-cut.mkv")]
    [DataRow("/media/movies/Movie.trailers.mkv")]
    [DataRow("/media/movies/Movie.samples.mkv")]
    public void ShouldIgnore_ReturnsFalse(string path)
    {
        Assert.IsFalse(PathFilter.ShouldIgnore(P(path)));
    }

    [TestMethod]
    [DataRow("/movies/live-action/Movie (2026)/Featurettes/Making Of.mkv")]
    [DataRow("/movies/live-action/Movie (2026)/featurettes/Making Of.mkv")]
    [DataRow("/movies/live-action/Movie (2026)/Behind The Scenes/Stunts.mkv")]
    [DataRow("/movies/live-action/Movie (2026)/Deleted Scenes/Alternate Ending.mkv")]
    [DataRow("/movies/live-action/Movie (2026)/Interviews/Director.mkv")]
    [DataRow("/movies/live-action/Movie (2026)/Scenes/Opening.mkv")]
    [DataRow("/movies/live-action/Movie (2026)/Shorts/Short.mkv")]
    [DataRow("/movies/live-action/Movie (2026)/Clips/Clip.mkv")]
    [DataRow("/movies/live-action/Movie (2026)/Extras/Gag Reel.mkv")]
    [DataRow("/tv/anime/Show (2026)/Season 01/Extras/Creditless OP.mkv")]
    [DataRow(@"C:\movies\Movie (2026)\Featurettes\Making Of.mkv")]
    public void IsExtra_ReturnsTrue(string path)
    {
        Assert.IsTrue(PathFilter.IsExtra(P(path)));
    }

    [TestMethod]
    [DataRow("/movies/live-action/Movie (2026)/Movie (2026).mkv")]
    // "other" is a library category, not an extras folder.
    [DataRow("/movies/other/Movie (2026)/Movie (2026).mkv")]
    [DataRow("/movies/other/Movie (2026)/Other/Thing.mkv")]
    [DataRow("/tv/live-action/Extras (2005)/Season 01/Extras S01E01.mkv")]
    [DataRow("/movies/live-action/Movie (2026)/Featurettes.mkv")]
    [DataRow("/movies/live-action/Movie (2026)/Extras.mkv")]
    public void IsExtra_ReturnsFalse(string path)
    {
        Assert.IsFalse(PathFilter.IsExtra(P(path)));
    }

    // --- User excluded folders ---

    private static readonly string[] Excluded = [".snapshots", "Old Rips", "/movies/other/incoming/"];

    [TestMethod]
    [DataRow("/movies/live-action/.snapshots/Movie (2026)/Movie (2026).mkv")]
    [DataRow("/movies/live-action/old rips/Movie.mkv")]
    [DataRow("/movies/other/incoming/Movie.mkv")]
    [DataRow("/movies/other/incoming/sub/Movie.mkv")]
    public void Excluded_ReturnsTrue(string path)
    {
        Assert.IsTrue(PathFilter.IsExcluded(P(path), Excluded));
        Assert.IsTrue(PathFilter.ShouldIgnore(P(path), Excluded));
    }

    [TestMethod]
    [DataRow("/movies/live-action/Movie (2026)/Movie (2026).mkv")]
    [DataRow("/movies/other/incoming-done/Movie.mkv")]
    [DataRow("/movies/other/Movie (2026)/incoming.mkv")]
    [DataRow("/movies/live-action/.snapshots.mkv")]
    [DataRow("/archive/movies/other/incoming/Movie.mkv")]
    public void Excluded_ReturnsFalse(string path)
    {
        Assert.IsFalse(PathFilter.IsExcluded(P(path), Excluded));
    }

    [TestMethod]
    public void Excluded_EmptyListExcludesNothing()
    {
        Assert.IsFalse(PathFilter.IsExcluded(P("/movies/a/b.mkv"), []));
        Assert.IsFalse(PathFilter.IsExcluded(P("/movies/a/b.mkv"), null));
        Assert.IsFalse(PathFilter.IsExcluded(P("/movies/a/b.mkv"), ["", "  ", "/"]));
    }

    [TestMethod]
    public void ParseExcludedFolders_SkipsBlanksAndComments()
    {
        var parsed = PathFilter.ParseExcludedFolders(" .snapshots \r\n\n# old stuff\r\n/movies/other/incoming\n");

        CollectionAssert.AreEqual(new[] { ".snapshots", "/movies/other/incoming" }, parsed);
    }
}
