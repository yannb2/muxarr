using Muxarr.Data.Entities;
using static Muxarr.Tests.TestData;

namespace Muxarr.Tests;

[TestClass]
public class MediaSnapshotOrderTests
{
    // SQLite returns child rows in whichever SnapshotId index it picks, which
    // can be (SnapshotId, Type, ...) order. Readers must still see source order.
    [TestMethod]
    public void Tracks_ReadBackInIndexOrder_WhateverOrderTheyWereLoaded()
    {
        var snapshot = new MediaSnapshot();
        snapshot.Tracks.Add(Audio(1, "English"));
        snapshot.Tracks.Add(Sub(2, "English"));
        snapshot.Tracks.Add(Video(0));

        CollectionAssert.AreEqual(new[] { 0, 1, 2 }, snapshot.Tracks.Select(t => t.Index).ToArray());
    }

    [TestMethod]
    public void Tracks_SortKeepsTheSameListInstance()
    {
        var list = new List<TrackSnapshot> { Audio(1, "English"), Video(0) };
        var snapshot = new MediaSnapshot { Tracks = list };

        Assert.AreSame(list, snapshot.Tracks);
        Assert.AreEqual(0, list[0].Index);
    }
}
