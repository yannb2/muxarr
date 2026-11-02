using Muxarr.Data.Entities;
using static Muxarr.Tests.TestData;

namespace Muxarr.Tests;

[TestClass]
public class MediaSnapshotOrderTests
{
    // SQLite returns child rows in whichever SnapshotId index it picks, which
    // can be (SnapshotId, Type, ...) order. A loaded snapshot still reads in source order.
    [TestMethod]
    public void LoadedSnapshot_TracksReadBackInIndexOrder()
    {
        var snapshot = new MediaSnapshot();
        snapshot.MarkLoadedFromDatabase();
        var list = snapshot.Tracks;
        list.Add(Audio(1, "English"));
        list.Add(Sub(2, "English"));
        list.Add(Video(0));

        CollectionAssert.AreEqual(new[] { 0, 1, 2 }, snapshot.Tracks.Select(t => t.Index).ToArray());
        Assert.AreSame(list, snapshot.Tracks, "sorted in place so EF keeps tracking the same list");
    }

    // Snapshots built in memory (a custom reorder) keep the order they were given.
    [TestMethod]
    public void InMemorySnapshot_KeepsItsOrder()
    {
        var snapshot = new MediaSnapshot { Tracks = [Video(0), Audio(2, "French"), Audio(1, "English")] };

        CollectionAssert.AreEqual(new[] { 0, 2, 1 }, snapshot.Tracks.Select(t => t.Index).ToArray());
    }
}
