using Muxarr.Core.Extensions;
using Muxarr.Core.Language;
using Muxarr.Core.Models;
using Muxarr.Data.Entities;
using Muxarr.Data.Extensions;
using static Muxarr.Tests.TestData;

namespace Muxarr.Tests;

[TestClass]
public class ExtrasAndCompatibilityTests
{
    private static TrackSettings EnglishAudio(bool keepCompatibility = true, int? maxTracks = null)
    {
        return new TrackSettings
        {
            Enabled = true,
            AllowedLanguages =
            [
                new LanguagePreference(IsoLanguage.Find("English")) { MaxTracks = maxTracks },
                IsoLanguage.OriginalLanguage
            ],
            RemoveCommentary = true,
            KeepCompatibilityTrack = keepCompatibility
        };
    }

    private static int[] Indexes(IEnumerable<TrackSnapshot> tracks)
    {
        return tracks.Select(t => t.Index).ToArray();
    }

    // --- Compatibility audio track ---

    [TestMethod]
    public void Compatibility_KeepsAc3NextToTrueHd_PastTheTrackLimit()
    {
        var tracks = new List<TrackSnapshot>
        {
            Audio(1, "English", nameof(AudioCodec.TrueHd), 8),
            Audio(2, "English", nameof(AudioCodec.Ac3), 6)
        };

        var result = tracks.GetAllowedTracks(EnglishAudio(maxTracks: 1), "English");

        CollectionAssert.AreEqual(new[] { 1, 2 }, Indexes(result));
    }

    [TestMethod]
    public void Compatibility_Off_TheTrackLimitWins()
    {
        var tracks = new List<TrackSnapshot>
        {
            Audio(1, "English", nameof(AudioCodec.TrueHd), 8),
            Audio(2, "English", nameof(AudioCodec.Ac3), 6)
        };

        var result = tracks.GetAllowedTracks(EnglishAudio(false, 1), "English");

        CollectionAssert.AreEqual(new[] { 1 }, Indexes(result));
    }

    [TestMethod]
    public void Compatibility_OverridesAnExcludedCodec()
    {
        var settings = EnglishAudio();
        settings.ExcludeCodecs = true;
        settings.ExcludedAudioCodecs = [AudioCodec.Ac3];
        var tracks = new List<TrackSnapshot>
        {
            Audio(1, "English", nameof(AudioCodec.DtsHdMa), 8),
            Audio(2, "English", nameof(AudioCodec.Ac3), 6)
        };

        var result = tracks.GetAllowedTracks(settings, "English");

        CollectionAssert.AreEqual(new[] { 1, 2 }, Indexes(result));
    }

    [TestMethod]
    public void Compatibility_PrefersEac3ThenMostChannels()
    {
        var tracks = new List<TrackSnapshot>
        {
            Audio(1, "English", nameof(AudioCodec.TrueHd), 8),
            Audio(2, "English", nameof(AudioCodec.Aac), 2),
            Audio(3, "English", nameof(AudioCodec.Ac3), 6),
            Audio(4, "English", nameof(AudioCodec.Eac3), 2),
            Audio(5, "English", nameof(AudioCodec.Eac3), 6)
        };

        var result = tracks.GetAllowedTracks(EnglishAudio(maxTracks: 1), "English");

        CollectionAssert.AreEqual(new[] { 1, 5 }, Indexes(result));
    }

    [TestMethod]
    public void Compatibility_NotAddedWhenALossyTrackIsAlreadyKept()
    {
        var tracks = new List<TrackSnapshot>
        {
            Audio(1, "English", nameof(AudioCodec.TrueHd), 8),
            Audio(2, "English", nameof(AudioCodec.Eac3), 6),
            Audio(3, "English", nameof(AudioCodec.Ac3), 6)
        };

        var result = tracks.GetAllowedTracks(EnglishAudio(maxTracks: 2), "English");

        CollectionAssert.AreEqual(new[] { 1, 2 }, Indexes(result));
    }

    [TestMethod]
    public void Compatibility_NeverPicksACommentaryOrAnotherLanguage()
    {
        var tracks = new List<TrackSnapshot>
        {
            Audio(1, "English", nameof(AudioCodec.TrueHd), 8),
            Audio(2, "English", nameof(AudioCodec.Ac3), 2, commentary: true),
            Audio(3, "French", nameof(AudioCodec.Ac3), 6)
        };

        var result = tracks.GetAllowedTracks(EnglishAudio(maxTracks: 1), "English");

        CollectionAssert.AreEqual(new[] { 1 }, Indexes(result));
    }

    [TestMethod]
    public void Compatibility_EachLanguageGetsItsOwnPartner()
    {
        var tracks = new List<TrackSnapshot>
        {
            Audio(1, "Japanese", nameof(AudioCodec.Flac), 2),
            Audio(2, "Japanese", nameof(AudioCodec.Aac), 2),
            Audio(3, "English", nameof(AudioCodec.TrueHd), 8),
            Audio(4, "English", nameof(AudioCodec.Eac3), 6)
        };
        var settings = new TrackSettings
        {
            Enabled = true,
            AllowedLanguages =
            [
                new LanguagePreference(IsoLanguage.OriginalLanguage) { MaxTracks = 1 },
                new LanguagePreference(IsoLanguage.Find("English")) { MaxTracks = 1 }
            ],
            KeepCompatibilityTrack = true
        };

        var result = tracks.GetAllowedTracks(settings, "Japanese");

        CollectionAssert.AreEqual(new[] { 1, 2, 3, 4 }, Indexes(result));
    }

    [TestMethod]
    public void Compatibility_LossyMainTrackNeedsNoPartner()
    {
        var tracks = new List<TrackSnapshot>
        {
            Audio(1, "English", nameof(AudioCodec.Eac3), 6),
            Audio(2, "English", nameof(AudioCodec.Aac), 2)
        };

        var result = tracks.GetAllowedTracks(EnglishAudio(maxTracks: 1), "English");

        CollectionAssert.AreEqual(new[] { 1 }, Indexes(result));
    }

    // --- Extras folders ---

    private static MediaFile Extra(string path)
    {
        var file = MakeFile("English",
            Video(0),
            Audio(1, "German", nameof(AudioCodec.Ac3), 2),
            Audio(2, "English", nameof(AudioCodec.Aac), 2),
            Sub(3, "Spanish"));
        file.Path = path;
        return file;
    }

    private static Profile ExtrasProfile(ExtrasStrategy extras)
    {
        var profile = MakeProfile(EnglishAudio(false), new TrackSettings
        {
            Enabled = true,
            AllowedLanguages = [IsoLanguage.Find("English")]
        });
        profile.Extras = extras;
        return profile;
    }

    [TestMethod]
    public void Extras_KeepAllTracks_KeepsEveryTrackInAnExtrasFolder()
    {
        var file = Extra("/movies/live-action/Movie (2026)/Featurettes/Making Of.mkv");
        var profile = ExtrasProfile(ExtrasStrategy.KeepAllTracks);
        file.Profile = profile;

        var target = file.BuildTargetFromProfile(profile);

        Assert.AreEqual(4, target.Tracks.Count);
    }

    [TestMethod]
    public void Extras_KeepAllTracks_MainFilesFollowTheRules()
    {
        var file = Extra("/movies/live-action/Movie (2026)/Movie (2026).mkv");
        var profile = ExtrasProfile(ExtrasStrategy.KeepAllTracks);
        file.Profile = profile;

        var target = file.BuildTargetFromProfile(profile);

        Assert.AreEqual(2, target.Tracks.Count, "video and the English audio");
    }

    [TestMethod]
    public void Extras_Process_ExtrasFollowTheRules()
    {
        var file = Extra("/movies/live-action/Movie (2026)/Featurettes/Making Of.mkv");
        var profile = ExtrasProfile(ExtrasStrategy.Process);
        file.Profile = profile;

        var target = file.BuildTargetFromProfile(profile);

        Assert.AreEqual(2, target.Tracks.Count);
    }

    [TestMethod]
    public void Extras_CloneCopiesTheStrategy()
    {
        var profile = ExtrasProfile(ExtrasStrategy.Skip);

        Assert.AreEqual(ExtrasStrategy.Skip, profile.Clone().Extras);
    }
}
