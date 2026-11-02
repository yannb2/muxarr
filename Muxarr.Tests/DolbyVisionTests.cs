using Muxarr.Core.Extensions;
using Muxarr.Core.FFmpeg;
using Muxarr.Core.Models;
using Muxarr.Core.Utilities;
using Muxarr.Data.Entities;
using Muxarr.Data.Extensions;
using static Muxarr.Tests.TestData;

namespace Muxarr.Tests;

[TestClass]
public class DolbyVisionTests
{
    // Trimmed from `ffprobe -show_streams` on a profile 7 FEL remux.
    private const string ProfileSevenStream = """
        {
          "streams": [
            {
              "index": 0,
              "codec_name": "hevc",
              "codec_type": "video",
              "profile": "Main 10",
              "width": 3840,
              "height": 2160,
              "color_transfer": "smpte2084",
              "side_data_list": [
                {
                  "side_data_type": "DOVI configuration record",
                  "dv_version_major": 1,
                  "dv_version_minor": 0,
                  "dv_profile": 7,
                  "dv_level": 6,
                  "rpu_present_flag": 1,
                  "el_present_flag": 1,
                  "bl_present_flag": 1,
                  "dv_bl_signal_compatibility_id": 6
                }
              ]
            }
          ]
        }
        """;

    [TestMethod]
    public void FFprobe_ParsesDoviRecordAndTransfer()
    {
        var result = JsonHelper.Deserialize<FFprobeResult>(ProfileSevenStream)!;
        var stream = result.Streams[0];
        var dovi = HdrInfo.FindDoviRecord(stream.SideDataList);

        Assert.AreEqual("smpte2084", stream.ColorTransfer);
        Assert.IsNotNull(dovi);
        Assert.AreEqual("7", HdrInfo.ParseDvProfile(dovi));
        Assert.IsTrue(HdrInfo.HasEnhancementLayer(dovi));
    }

    [TestMethod]
    [DataRow(8, 1, "8.1")]
    [DataRow(8, 4, "8.4")]
    [DataRow(8, 0, "8")]
    [DataRow(5, 0, "5")]
    [DataRow(7, 6, "7")]
    public void ParseDvProfile_AddsProfileEightCompatibility(int profile, int compat, string expected)
    {
        var dovi = new FFprobeSideData
        {
            SideDataType = FFprobeSideData.DoviConfigurationRecord,
            DvProfile = profile,
            DvBlSignalCompatibilityId = compat
        };

        Assert.AreEqual(expected, HdrInfo.ParseDvProfile(dovi));
    }

    [TestMethod]
    public void HasEnhancementLayer_OnlyProfileSevenWithEl()
    {
        Assert.IsFalse(HdrInfo.HasEnhancementLayer(new FFprobeSideData { DvProfile = 8, ElPresentFlag = 0 }));
        Assert.IsFalse(HdrInfo.HasEnhancementLayer(new FFprobeSideData { DvProfile = 7, ElPresentFlag = 0 }));
        Assert.IsFalse(HdrInfo.HasEnhancementLayer(null));
    }

    [TestMethod]
    [DataRow("smpte2084", "HDR10")]
    [DataRow("arib-std-b67", "HLG")]
    [DataRow("bt709", "")]
    [DataRow("", "")]
    public void ParseHdrFormat(string transfer, string expected)
    {
        Assert.AreEqual(expected, HdrInfo.ParseHdrFormat(transfer) ?? "");
    }

    // Trimmed from `ffprobe -show_frames -read_intervals %+#2` on an HDR10+ HEVC file.
    private const string Hdr10PlusFrames = """
        {
          "frames": [
            {
              "media_type": "video",
              "side_data_list": [
                { "side_data_type": "Mastering display metadata", "red_x": "34000/50000" },
                {
                  "side_data_type": "HDR Dynamic Metadata SMPTE2094-40 (HDR10+)",
                  "application_version": 1,
                  "num_windows": 1,
                  "targeted_system_display_maximum_luminance": "400/1"
                }
              ]
            }
          ]
        }
        """;

    private const string Hdr10OnlyFrames = """
        {
          "frames": [
            { "media_type": "video", "side_data_list": [ { "side_data_type": "Mastering display metadata" } ] },
            { "media_type": "video" }
          ]
        }
        """;

    [TestMethod]
    public void HasHdr10Plus_FindsTheDynamicMetadata()
    {
        Assert.IsTrue(HdrInfo.HasHdr10Plus(JsonHelper.Deserialize<FFprobeFramesResult>(Hdr10PlusFrames)));
        Assert.IsFalse(HdrInfo.HasHdr10Plus(JsonHelper.Deserialize<FFprobeFramesResult>(Hdr10OnlyFrames)));
        Assert.IsFalse(HdrInfo.HasHdr10Plus(null));
    }

    [TestMethod]
    public void Template_DvWithHdr10Plus()
    {
        var track = DvVideo("8.1", null);
        track.HdrFormat = HdrInfo.Hdr10Plus;

        Assert.AreEqual("HEVC DV P8.1 HDR10+", track.ApplyTrackNameTemplate("{codec} {dv} {hdr}",
            new Dictionary<string, string> { ["H.265 / HEVC"] = "HEVC" }));
    }

    [TestMethod]
    [DataRow("Summary:\n  Frames: 500\n  Profile: 7 (FEL)\n  DM version: 1 (CM v2.9)", "FEL")]
    [DataRow("Summary:\n  Frames: 500\n  Profile: 7 (MEL)\n  DM version: 1 (CM v2.9)", "MEL")]
    [DataRow("Summary:\n  Frames: 500\n  Profile: 7 (FEL, MEL)", "FEL")]
    [DataRow("Summary:\n  Frames: 500\n  Profile: 8\n  DM version: 1 (CM v2.9)", "")]
    [DataRow("", "")]
    public void ParseDoviToolSummary(string summary, string expected)
    {
        Assert.AreEqual(expected, HdrInfo.ParseDoviToolSummary(summary) ?? "");
    }

    [TestMethod]
    [DataRow("7", "FEL", "DV P7 FEL")]
    [DataRow("7", "", "DV P7")]
    [DataRow("8.1", "", "DV P8.1")]
    [DataRow("", "", "")]
    public void FormatDolbyVision(string profile, string layer, string expected)
    {
        Assert.AreEqual(expected, HdrInfo.FormatDolbyVision(profile, layer));
    }

    // --- Video track names ---

    [TestMethod]
    public void Template_VideoTokens()
    {
        var track = DvVideo("7", HdrInfo.Fel);
        var names = new Dictionary<string, string> { ["H.265 / HEVC"] = "HEVC" };

        Assert.AreEqual("HEVC DV P7 FEL HDR10", track.ApplyTrackNameTemplate("{codec} {dv} {hdr}", names));
        Assert.AreEqual("7 FEL", track.ApplyTrackNameTemplate("{dvprofile} {dvlayer}"));
    }

    [TestMethod]
    public void Template_NonDvVideo_CollapsesEmptyTokens()
    {
        var track = Video(0);
        track.HdrFormat = HdrInfo.Hdr10;

        Assert.AreEqual("H.265 / HEVC HDR10", track.ApplyTrackNameTemplate("{codec} {dv} {hdr}"));
    }

    [TestMethod]
    public void Profile_StandardizedVideoName_WinsOverClear()
    {
        var profile = new Profile
        {
            ClearVideoTrackNames = true,
            VideoSettings = new TrackSettings
            {
                StandardizeTrackNames = true,
                TrackNameTemplate = "{codec} {dv} {hdr}",
                CodecNames = new Dictionary<string, string> { ["H.265 / HEVC"] = "HEVC" }
            },
            AudioSettings = new TrackSettings(),
            SubtitleSettings = new TrackSettings()
        };
        var video = DvVideo("8.1", null);
        video.Name = "Release Group Encode";
        var file = MakeFile("English", video, Audio(1, "English"));

        var plan = file.BuildTargetFromProfile(profile);

        Assert.AreEqual("HEVC DV P8.1 HDR10", plan.Tracks.Single(t => t.Type == MediaTrackType.Video).Name);
    }

    [TestMethod]
    public void Profile_ClearVideoNames_StillClearsWithoutTemplate()
    {
        var profile = new Profile
        {
            ClearVideoTrackNames = true,
            AudioSettings = new TrackSettings(),
            SubtitleSettings = new TrackSettings()
        };
        var file = MakeFile("English", Video(0, "Release Group Encode"), Audio(1, "English"));

        var plan = file.BuildTargetFromProfile(profile);

        Assert.AreEqual("", plan.Tracks.Single(t => t.Type == MediaTrackType.Video).Name);
    }

    private static TrackSnapshot DvVideo(string profile, string? layer)
    {
        var track = Video(0);
        track.HdrFormat = HdrInfo.Hdr10;
        track.DvProfile = profile;
        track.DvLayer = layer;
        return track;
    }
}
