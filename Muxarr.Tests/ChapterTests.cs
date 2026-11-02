using System.Xml.Linq;
using Muxarr.Core.Models;
using Muxarr.Core.MkvToolNix;
using Muxarr.Data.Entities;
using Muxarr.Data.Extensions;
using Muxarr.Web.Components.Shared.Modals;
using Muxarr.Web.Services;
using static Muxarr.Tests.TestData;

namespace Muxarr.Tests;

/// <summary>
/// Chapter renaming: the XML rewrite, what the planner makes of a rename, and
/// what the custom conversion window puts in the plan.
/// </summary>
[TestClass]
public class ChapterTests
{
    private const string ExtractedXml = """
        <?xml version="1.0"?>
        <Chapters>
          <EditionEntry>
            <EditionUID>1</EditionUID>
            <ChapterAtom>
              <ChapterUID>11</ChapterUID>
              <ChapterTimeStart>00:00:00.000000000</ChapterTimeStart>
              <ChapterDisplay>
                <ChapterString>Chapter 1</ChapterString>
                <ChapterLanguage>eng</ChapterLanguage>
              </ChapterDisplay>
              <ChapterDisplay>
                <ChapterString>Chapitre 1</ChapterString>
                <ChapterLanguage>fre</ChapterLanguage>
              </ChapterDisplay>
            </ChapterAtom>
            <ChapterAtom>
              <ChapterUID>12</ChapterUID>
              <ChapterTimeStart>00:05:12.345678901</ChapterTimeStart>
              <ChapterFlagHidden>1</ChapterFlagHidden>
            </ChapterAtom>
            <ChapterAtom>
              <ChapterUID>13</ChapterUID>
              <ChapterTimeStart>01:02:03.004000000</ChapterTimeStart>
              <ChapterDisplay>
                <ChapterString>Chapter 3</ChapterString>
              </ChapterDisplay>
            </ChapterAtom>
          </EditionEntry>
        </Chapters>
        """;

    [TestMethod]
    [DataRow("00:00:00.000000000", 0L)]
    [DataRow("00:05:12.345678901", 312_346L)]
    [DataRow("01:02:03.004000000", 3_723_004L)]
    [DataRow("1:02:03.5", 3_723_500L)]
    public void ParseTime_ReadsMatroskaTimestamps(string value, long expected)
    {
        Assert.AreEqual(expected, MkvChapters.ParseTime(value));
    }

    [TestMethod]
    public void ParseTime_RejectsGarbage()
    {
        Assert.IsNull(MkvChapters.ParseTime("soon"));
        Assert.IsNull(MkvChapters.ParseTime(null));
    }

    [TestMethod]
    public void Rename_ChangesOnlyTheMatchedTitles()
    {
        var doc = XDocument.Parse(ExtractedXml);

        var matched = MkvChapters.Rename(doc,
        [
            new ChapterInfo { StartMs = 0, Title = "Opening" },
            new ChapterInfo { StartMs = 312_345, Title = "The Heist" },
            new ChapterInfo { StartMs = 3_723_004, Title = null }
        ]);

        Assert.AreEqual(2, matched);
        var atoms = doc.Descendants("ChapterAtom").ToList();
        var first = atoms[0].Elements("ChapterDisplay").ToList();
        Assert.AreEqual("Opening", first[0].Element("ChapterString")!.Value);
        Assert.AreEqual("Chapitre 1", first[1].Element("ChapterString")!.Value, "other languages stay");
        Assert.AreEqual("The Heist", atoms[1].Element("ChapterDisplay")!.Element("ChapterString")!.Value,
            "a chapter with no title gets one");
        Assert.AreEqual("1", atoms[1].Element("ChapterFlagHidden")!.Value, "flags stay");
        Assert.AreEqual("Chapter 3", atoms[2].Element("ChapterDisplay")!.Element("ChapterString")!.Value,
            "no title wanted, nothing changed");
    }

    [TestMethod]
    public void Load_IgnoresTheDoctypeMkvextractWrites()
    {
        var path = Path.Combine(Path.GetTempPath(), $"muxarr-chapters-test-{Guid.NewGuid():N}.xml");
        File.WriteAllText(path, """
            <?xml version="1.0"?>
            <!DOCTYPE Chapters SYSTEM "matroskachapters.dtd">
            <Chapters><EditionEntry><ChapterAtom><ChapterTimeStart>00:00:00.000000000</ChapterTimeStart></ChapterAtom></EditionEntry></Chapters>
            """);
        try
        {
            var doc = MkvChapters.Load(path);
            Assert.AreEqual(1, doc.Descendants("ChapterAtom").Count());
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static MediaFile ChapteredFile(string container = "Matroska")
    {
        var file = MakeFile("English", Video(0), Audio(1));
        file.Snapshot.ContainerType = container;
        file.Snapshot.HasChapters = true;
        file.Snapshot.Chapters =
        [
            new ChapterInfo { StartMs = 0, EndMs = 1000, Title = "Chapter 1" },
            new ChapterInfo { StartMs = 1000, EndMs = 2000, Title = "Chapter 2" }
        ];
        return file;
    }

    [TestMethod]
    public void Planner_RenameIsAMetadataEdit()
    {
        var file = ChapteredFile();
        var target = file.ToTargetSnapshotFromSource();
        target.Chapters = [new ChapterInfo { StartMs = 1, Title = "Opening" }];

        var plan = ConversionPlanner.Plan(file.Snapshot, target);

        Assert.AreEqual(ConversionPlanner.ConversionStrategy.MetadataEdit, plan.Strategy);
        Assert.IsNotNull(plan.Delta.Chapters);
    }

    [TestMethod]
    public void Planner_AlreadyRenamedOrMovedChaptersAreDone()
    {
        var file = ChapteredFile();
        var target = file.ToTargetSnapshotFromSource();
        target.Chapters =
        [
            new ChapterInfo { StartMs = 0, Title = "Chapter 1" },
            new ChapterInfo { StartMs = 60_000, Title = "Not in this file" }
        ];

        Assert.AreEqual(ConversionPlanner.ConversionStrategy.Skip, ConversionPlanner.Plan(file.Snapshot, target).Strategy);
    }

    [TestMethod]
    public void Planner_IgnoresChapterNamesOutsideMatroska()
    {
        var file = ChapteredFile("MP4/QuickTime");
        var target = file.ToTargetSnapshotFromSource();
        target.Chapters = [new ChapterInfo { StartMs = 0, Title = "Opening" }];

        var plan = ConversionPlanner.Plan(file.Snapshot, target);

        Assert.IsNull(plan.Delta.Chapters);
    }

    [TestMethod]
    public void Editor_OnlyChangedTitlesReachThePlan()
    {
        var editor = new CustomConversionEditor(ChapteredFile());
        Assert.IsTrue(editor.CanRenameChapters);
        Assert.IsNull(editor.Build().Chapters, "nothing renamed, nothing planned");

        editor.Chapters[1].Title = "  Finale ";
        editor.Chapters[0].Title = "";
        var chapters = editor.Build().Chapters!;

        Assert.IsNull(chapters[0].Title, "an empty box keeps the title");
        Assert.AreEqual("Finale", chapters[1].Title);
        Assert.AreEqual(1000, chapters[1].StartMs);
    }

    [TestMethod]
    public void Editor_NumberChapters()
    {
        var editor = new CustomConversionEditor(ChapteredFile());

        editor.NumberChapters("Part");

        CollectionAssert.AreEqual(new[] { "Part 1", "Part 2" }, editor.Chapters.Select(c => c.Title).ToArray());
        editor.ResetChapters();
        Assert.IsNull(editor.Build().Chapters);
    }

    [TestMethod]
    public void Editor_NoChapterRenamesOnMp4()
    {
        Assert.IsFalse(new CustomConversionEditor(ChapteredFile("MP4/QuickTime")).CanRenameChapters);
    }
}
