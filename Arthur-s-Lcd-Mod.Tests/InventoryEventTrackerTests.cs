using LcdMod.Client.GridData;

namespace Arthur_s_Lcd_Mod.Tests;

public sealed class InventoryEventTrackerTests
{
    [Fact]
    public void DetailedThenGeneric_IsMatched()
    {
        var tracker = new InventoryEventTracker<int>();
        tracker.RecordDetailedChange(7);

        Assert.True(tracker.CompleteContentsChange(7));
    }

    [Fact]
    public void GenericWithoutDetailed_IsNotMatched()
    {
        var tracker = new InventoryEventTracker<int>();

        Assert.False(tracker.CompleteContentsChange(7));
    }

    [Fact]
    public void MultipleDetailedEvents_AreCompletedByOneGenericEvent()
    {
        var tracker = new InventoryEventTracker<int>();
        tracker.RecordDetailedChange(7);
        tracker.RecordDetailedChange(7);

        Assert.True(tracker.CompleteContentsChange(7));
        Assert.False(tracker.CompleteContentsChange(7));
    }

    [Fact]
    public void Inventories_AreTrackedIndependently()
    {
        var tracker = new InventoryEventTracker<int>();
        tracker.RecordDetailedChange(7);

        Assert.False(tracker.CompleteContentsChange(8));
        Assert.True(tracker.CompleteContentsChange(7));
    }

    [Fact]
    public void Forget_PreventsStaleDetailedMatch()
    {
        var tracker = new InventoryEventTracker<int>();
        tracker.RecordDetailedChange(7);
        tracker.Forget(7);

        Assert.False(tracker.CompleteContentsChange(7));
    }
}
