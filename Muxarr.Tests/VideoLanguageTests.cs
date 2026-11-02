using Muxarr.Core.Language;
using Muxarr.Core.Models;
using Muxarr.Data.Entities;
using Muxarr.Data.Extensions;
using static Muxarr.Tests.TestData;

namespace Muxarr.Tests;

[TestClass]
public class VideoLanguageTests
{
    private static TrackPlan VideoTarget(MediaFile file, VideoLanguageMode mode)
    {
        var profile = MakeProfile();
        profile.VideoSettings.VideoLanguage = mode;
        file.Profile = profile;
        return file.BuildTargetFromProfile(profile).Tracks.Single(t => t.Type == MediaTrackType.Video);
    }

    [TestMethod]
    public void OriginalLanguage_SetsTheVideoTrack()
    {
        var file = MakeFile("French", Video(0));

        var video = VideoTarget(file, VideoLanguageMode.OriginalLanguage);

        Assert.AreEqual(IsoLanguage.Find("French").WriteCode, video.LanguageCode);
        Assert.IsTrue(file.CheckHasNonStandardMetadata(file.Profile), "the change marks the file for work");
    }

    [TestMethod]
    public void OriginalLanguage_UnknownLeavesItAlone()
    {
        var file = MakeFile(null, Video(0));

        var video = VideoTarget(file, VideoLanguageMode.OriginalLanguage);

        Assert.AreEqual("und", video.LanguageCode);
        Assert.IsFalse(file.CheckHasNonStandardMetadata(file.Profile));
    }

    [TestMethod]
    [DataRow("eng")]
    [DataRow("en")]
    public void OriginalLanguage_AlreadySetIsNotAChange(string code)
    {
        var track = Video(0);
        track.LanguageCode = code;
        track.LanguageName = "English";
        var file = MakeFile("English", track);

        var video = VideoTarget(file, VideoLanguageMode.OriginalLanguage);

        Assert.AreEqual(code, video.LanguageCode);
        Assert.IsFalse(file.CheckHasNonStandardMetadata(file.Profile));
    }

    [TestMethod]
    public void Undetermined_ClearsALanguage()
    {
        var track = Video(0);
        track.LanguageCode = "eng";
        track.LanguageName = "English";
        var file = MakeFile("English", track);

        var video = VideoTarget(file, VideoLanguageMode.Undetermined);

        Assert.AreEqual("und", video.LanguageCode);
    }

    [TestMethod]
    public void Leave_ChangesNothing()
    {
        var file = MakeFile("French", Video(0));

        var video = VideoTarget(file, VideoLanguageMode.Leave);

        Assert.AreEqual("und", video.LanguageCode);
        Assert.IsFalse(file.CheckHasNonStandardMetadata(file.Profile));
    }
}
