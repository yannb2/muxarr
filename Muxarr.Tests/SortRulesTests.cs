using Muxarr.Core.Extensions;
using Muxarr.Core.Language;
using Muxarr.Core.Models;
using Muxarr.Data.Entities;
using Muxarr.Data.Extensions;
using static Muxarr.Tests.TestData;

namespace Muxarr.Tests;

[TestClass]
public class SortRulesTests
{
    private static TrackSettings YannsSubtitleSettings() => new()
    {
        Enabled = true,
        AllowedLanguages = [IsoLanguage.Find("English"), IsoLanguage.Find("French")],
        ReorderStrategy = TrackReorderStrategy.CustomRules,
        SortKeys = [TrackSortKey.ForcedFirst, TrackSortKey.LanguagePriority, TrackSortKey.CodecOrder],
        SubtitleCodecOrder = [SubtitleCodec.Pgs, SubtitleCodec.Srt]
    };

    [TestMethod]
    public void CustomRules_ForcedThenLanguageThenPgsBeforeSrt()
    {
        var tracks = new List<TrackSnapshot>
        {
            Sub(2, "French", nameof(SubtitleCodec.Srt)),
            Sub(3, "English", nameof(SubtitleCodec.Srt)),
            Sub(4, "French", nameof(SubtitleCodec.Pgs)),
            Sub(5, "English", nameof(SubtitleCodec.Pgs)),
            Sub(6, "English", nameof(SubtitleCodec.Pgs), forced: true)
        };

        var result = tracks.GetAllowedTracks(YannsSubtitleSettings(), "English");

        // Forced (English), English PGS, English SRT, French PGS, French SRT
        CollectionAssert.AreEqual(new[] { 6, 5, 3, 4, 2 }, result.Select(t => t.Index).ToArray());
    }

    [TestMethod]
    public void CustomRules_LanguageFirst_PutsForcedFirstWithinEachLanguage()
    {
        var settings = YannsSubtitleSettings();
        settings.SortKeys = [TrackSortKey.LanguagePriority, TrackSortKey.ForcedFirst, TrackSortKey.CodecOrder];
        var tracks = new List<TrackSnapshot>
        {
            Sub(2, "French", nameof(SubtitleCodec.Pgs), forced: true),
            Sub(3, "English", nameof(SubtitleCodec.Srt)),
            Sub(4, "French", nameof(SubtitleCodec.Pgs)),
            Sub(5, "English", nameof(SubtitleCodec.Pgs), forced: true)
        };

        var result = tracks.GetAllowedTracks(settings, "English");

        CollectionAssert.AreEqual(new[] { 5, 3, 2, 4 }, result.Select(t => t.Index).ToArray());
    }

    [TestMethod]
    public void CustomRules_EmptyKeys_UseDefaults()
    {
        var settings = YannsSubtitleSettings();
        settings.SortKeys = [];

        CollectionAssert.AreEqual(TrackSettings.DefaultSortKeys.ToArray(), settings.EffectiveSortKeys.ToArray());
    }

    [TestMethod]
    public void CustomRules_UnlistedCodecsFollowInBuiltInOrder()
    {
        var settings = YannsSubtitleSettings();
        settings.SortKeys = [TrackSortKey.CodecOrder];
        settings.SubtitleCodecOrder = [SubtitleCodec.Pgs];
        var tracks = new List<TrackSnapshot>
        {
            Sub(2, "English", nameof(SubtitleCodec.Ass)),
            Sub(3, "English", nameof(SubtitleCodec.Srt)),
            Sub(4, "English", nameof(SubtitleCodec.Pgs))
        };

        var result = tracks.GetAllowedTracks(settings, "English");

        // PGS is listed; SRT comes before ASS in the built-in enum order.
        CollectionAssert.AreEqual(new[] { 4, 3, 2 }, result.Select(t => t.Index).ToArray());
    }

    [TestMethod]
    public void CustomRules_SdhAndCommentaryLast()
    {
        var settings = new TrackSettings
        {
            ReorderStrategy = TrackReorderStrategy.CustomRules,
            SortKeys = [TrackSortKey.CommentaryLast, TrackSortKey.HearingImpairedLast]
        };
        var tracks = new List<TrackSnapshot>
        {
            Sub(2, "English", commentary: true),
            Sub(3, "English", hi: true),
            Sub(4, "English")
        };

        var result = tracks.GetAllowedTracks(settings, "English");

        CollectionAssert.AreEqual(new[] { 4, 3, 2 }, result.Select(t => t.Index).ToArray());
    }

    [TestMethod]
    public void CustomRules_Audio_BestQualityFirst()
    {
        var settings = new TrackSettings
        {
            ReorderStrategy = TrackReorderStrategy.CustomRules,
            SortKeys = [TrackSortKey.BestQualityFirst]
        };
        var tracks = new List<TrackSnapshot>
        {
            Audio(1, "English", nameof(AudioCodec.Ac3), 6),
            Audio(2, "English", nameof(AudioCodec.TrueHd), 8)
        };

        var result = tracks.GetAllowedTracks(settings, "English");

        CollectionAssert.AreEqual(new[] { 2, 1 }, result.Select(t => t.Index).ToArray());
    }

    // --- Forced only default ---

    [TestMethod]
    public void DefaultFlag_ForcedOnly_ClearsNonForcedSubtitles()
    {
        var profile = new Profile
        {
            AudioSettings = new TrackSettings { Enabled = false },
            SubtitleSettings = new TrackSettings
            {
                Enabled = false,
                DefaultStrategy = DefaultTrackStrategy.ForcedOnly
            }
        };

        var full = Sub(2, "English", nameof(SubtitleCodec.Pgs));
        full.IsDefault = true;
        var forced = Sub(3, "English", nameof(SubtitleCodec.Pgs), forced: true);
        forced.IsDefault = true;
        var file = MakeFile("English", Audio(1, "English"), full, forced);

        var subs = file.BuildTargetFromProfile(profile).Tracks
            .Where(t => t.Type == MediaTrackType.Subtitles)
            .ToDictionary(t => t.Index);

        Assert.AreEqual(false, subs[2].IsDefault);
        Assert.AreNotEqual(false, subs[3].IsDefault); // forced keeps its flag
    }

    [TestMethod]
    public void DefaultFlag_ForcedOnly_LeavesAudioAlone()
    {
        var profile = new Profile
        {
            AudioSettings = new TrackSettings { Enabled = false, DefaultStrategy = DefaultTrackStrategy.ForcedOnly },
            SubtitleSettings = new TrackSettings { Enabled = false }
        };

        var file = MakeFile("English", Audio(1, "English", isDefault: true));

        var audio = file.BuildTargetFromProfile(profile).Tracks.Single(t => t.Type == MediaTrackType.Audio);

        Assert.AreNotEqual(false, audio.IsDefault);
    }
}
