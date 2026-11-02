using Muxarr.Core.Extensions;
using Muxarr.Core.Models;
using Muxarr.Data.Entities;
using Muxarr.Data.Extensions;
using static Muxarr.Tests.TestData;

namespace Muxarr.Tests;

[TestClass]
public class CodecNamesTests
{
    // --- Detection from the ffprobe profile ---

    [TestMethod]
    [DataRow("Dolby TrueHD + Dolby Atmos", AudioFeature.Atmos)]
    [DataRow("Dolby Digital Plus + Dolby Atmos", AudioFeature.Atmos)]
    [DataRow("DTS-HD MA + DTS:X", AudioFeature.DtsX)]
    [DataRow("DTS-HD MA + DTS:X IMAX", AudioFeature.DtsX)]
    [DataRow("DTS-HD MA", AudioFeature.None)]
    [DataRow("LC", AudioFeature.None)]
    [DataRow("", AudioFeature.None)]
    [DataRow(null, AudioFeature.None)]
    public void ParseAudioFeature_ReadsFfprobeProfile(string? profile, AudioFeature expected)
    {
        Assert.AreEqual(expected, AudioFeatureExtensions.ParseAudioFeature(profile));
    }

    // --- Labels and custom names ---

    [TestMethod]
    public void Label_AddsAtmosToTheCoreCodec()
    {
        Assert.AreEqual("TrueHD Atmos", CodecNames.Label(Atmos(nameof(AudioCodec.TrueHd))));
        Assert.AreEqual("E-AC-3 Atmos", CodecNames.Label(Atmos(nameof(AudioCodec.Eac3))));
        Assert.AreEqual("TrueHD", CodecNames.Label(Audio(codec: nameof(AudioCodec.TrueHd))));
    }

    [TestMethod]
    public void Label_DtsX()
    {
        var track = Audio(codec: nameof(AudioCodec.DtsHdMa));
        track.AudioFeature = AudioFeature.DtsX;

        Assert.AreEqual("DTS:X", CodecNames.Label(track));
    }

    [TestMethod]
    public void Resolve_NoCustomNames_KeepsBuiltInLabel()
    {
        Assert.AreEqual("AC-3", CodecNames.Resolve(Audio(codec: nameof(AudioCodec.Ac3)), null));
        Assert.AreEqual("TrueHD Atmos", CodecNames.Resolve(Atmos(nameof(AudioCodec.TrueHd)), new Dictionary<string, string>()));
    }

    [TestMethod]
    public void Resolve_FullLabelWins()
    {
        var names = new Dictionary<string, string>
        {
            ["TrueHD"] = "Dolby TrueHD",
            ["TrueHD Atmos"] = "Dolby TrueHD with Atmos"
        };

        Assert.AreEqual("Dolby TrueHD with Atmos", CodecNames.Resolve(Atmos(nameof(AudioCodec.TrueHd)), names));
        Assert.AreEqual("Dolby TrueHD", CodecNames.Resolve(Audio(codec: nameof(AudioCodec.TrueHd)), names));
    }

    [TestMethod]
    public void Resolve_AtmosFallsBackToCoreNamePlusAtmos()
    {
        var names = new Dictionary<string, string> { ["E-AC-3"] = "Dolby EAC3" };

        Assert.AreEqual("Dolby EAC3 Atmos", CodecNames.Resolve(Atmos(nameof(AudioCodec.Eac3)), names));
    }

    [TestMethod]
    public void Resolve_IgnoresCaseAndBlankEntries()
    {
        var names = new Dictionary<string, string> { ["ac-3"] = "Dolby AC-3", ["AAC"] = "  " };

        Assert.AreEqual("Dolby AC-3", CodecNames.Resolve(Audio(codec: nameof(AudioCodec.Ac3)), names));
        Assert.AreEqual("AAC", CodecNames.Resolve(Audio(codec: nameof(AudioCodec.Aac)), names));
    }

    [TestMethod]
    public void Labels_Audio_ListsAtmosAfterItsCodec()
    {
        var labels = CodecNames.Labels(MediaTrackType.Audio).ToList();

        Assert.AreEqual(labels.IndexOf("TrueHD") + 1, labels.IndexOf("TrueHD Atmos"));
        Assert.AreEqual(labels.IndexOf("E-AC-3") + 1, labels.IndexOf("E-AC-3 Atmos"));
        Assert.AreEqual(labels.IndexOf("DTS-HD Master Audio") + 1, labels.IndexOf("DTS:X"));
        Assert.IsFalse(labels.Contains("Unknown"));
    }

    // --- Template placeholders ---

    [TestMethod]
    public void Template_YannsAudioNames()
    {
        var names = new Dictionary<string, string>
        {
            ["TrueHD"] = "Dolby TrueHD",
            ["E-AC-3"] = "Dolby EAC3",
            ["AC-3"] = "Dolby AC-3"
        };
        const string template = "{language} - {codec} {channels}";

        var truehd = Atmos(nameof(AudioCodec.TrueHd), channels: 8);
        var ac3 = Audio(codec: nameof(AudioCodec.Ac3), channels: 6);

        Assert.AreEqual("English - Dolby TrueHD Atmos 7.1", truehd.ApplyTrackNameTemplate(template, names));
        Assert.AreEqual("English - Dolby AC-3 5.1", ac3.ApplyTrackNameTemplate(template, names));
    }

    [TestMethod]
    public void Template_ObjectPlaceholder()
    {
        var track = Atmos(nameof(AudioCodec.Eac3), channels: 6);

        Assert.AreEqual("English Atmos", track.ApplyTrackNameTemplate("{language} {object}"));
        Assert.AreEqual("English", Audio().ApplyTrackNameTemplate("{language} {object}"));
    }

    [TestMethod]
    public void Template_WithoutCodecNames_MatchesUpstream()
    {
        var track = Audio(codec: nameof(AudioCodec.Ac3), channels: 6);

        Assert.AreEqual("English AC-3 5.1", track.ApplyTrackNameTemplate("{language} {codec} {channels}"));
    }

    // --- {description} ---

    [TestMethod]
    [DataRow(null, "")]
    [DataRow("Commentary by John Carpenter", "Commentary by John Carpenter")]
    [DataRow("English - Dolby AC-3 2.0 (Commentary by John Carpenter)", "Commentary by John Carpenter")]
    [DataRow("(Commentary)", "Commentary")]
    [DataRow("[Commentary]", "Commentary")]
    [DataRow("Commentary (Director)", "Commentary (Director)")]
    public void TrackDescription_StripsOwnPrefix(string? name, string expected)
    {
        Assert.AreEqual(expected, MediaFileExtensions.TrackDescription(name));
    }

    [TestMethod]
    public void Template_DescriptionDoesNotNestOnRerun()
    {
        var names = new Dictionary<string, string> { ["AC-3"] = "Dolby AC-3" };
        const string template = "{language} - {codec} {channels} ({description})";
        var track = Audio(codec: nameof(AudioCodec.Ac3), channels: 2, commentary: true,
            trackName: "Commentary by John Carpenter");

        var first = track.ApplyTrackNameTemplate(template, names);
        track.Name = first;
        var second = track.ApplyTrackNameTemplate(template, names);

        Assert.AreEqual("English - Dolby AC-3 2.0 (Commentary by John Carpenter)", first);
        Assert.AreEqual(first, second);
    }

    // --- Quality scoring ---

    [TestMethod]
    public void QualityScore_DetectedAtmos_CountsWithoutTheWordInTheName()
    {
        var atmos = Atmos(nameof(AudioCodec.TrueHd), channels: 8);
        atmos.Name = "English - Dolby TrueHD 7.1";
        var plain = Audio(codec: nameof(AudioCodec.TrueHd), channels: 8);

        Assert.IsTrue(TrackQualityScorer.ScoreAudio(atmos) > TrackQualityScorer.ScoreAudio(plain));
    }

    private static TrackSnapshot Atmos(string codec, int channels = 8)
    {
        var track = Audio(codec: codec, channels: channels);
        track.AudioFeature = AudioFeature.Atmos;
        return track;
    }
}
