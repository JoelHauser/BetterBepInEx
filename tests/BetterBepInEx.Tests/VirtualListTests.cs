using BetterBepInEx.Core;

namespace BetterBepInEx.Tests;

public class VirtualListTests
{
    private static VirtualList Rows(int count, float height, bool known = true)
    {
        var list = new VirtualList();
        for (int i = 0; i < count; i++)
        {
            list.Add(height, known);
        }
        return list;
    }

    [Fact]
    public void OnlyTheBlocksInViewAreDrawn()
    {
        var list = Rows(1000, 50f);
        list.Arrange(scroll: 5000f, view: 500f, below: 100f, above: 500f, minHeight: 20f, spare: 0);

        Assert.Equal(100, list.First);
        Assert.Equal(112, list.End);
        Assert.Equal(5000f, list.TopSpace);
        Assert.Equal(50000f - 112 * 50f, list.BottomSpace);
        Assert.Empty(list.Offscreen);
    }

    [Fact]
    public void SpacesAndDrawnBlocksAddUpToTheWholeList()
    {
        var list = new VirtualList();
        for (int i = 0; i < 300; i++)
        {
            list.Add(30f + i % 7 * 11f, i % 3 != 0);
        }
        foreach (float scroll in new[] { 0f, 123f, 4000f, list.Total - 400f })
        {
            list.Arrange(scroll, 400f, 200f, 400f, 20f, 5);
            float drawn = 0f;
            for (int i = list.First; i < list.End; i++)
            {
                drawn += list.Height(i);
            }
            Assert.Equal(list.Total, list.TopSpace + drawn + list.BottomSpace, 3);
        }
    }

    [Fact]
    public void APartlyVisibleTopBlockIsDrawn()
    {
        var list = Rows(100, 50f);
        list.Arrange(scroll: 125f, view: 200f, below: 0f, above: 0f, minHeight: 20f, spare: 0);

        Assert.Equal(2, list.First);
        Assert.Equal(100f, list.TopSpace);
    }

    [Fact]
    public void UnmeasuredBlocksReachFarEnoughToFillTheView()
    {
        // Estimated at 100 each, but they might be as short as 20, so five are not enough.
        var list = Rows(100, 100f, known: false);
        list.Arrange(scroll: 0f, view: 500f, below: 0f, above: 0f, minHeight: 20f, spare: 0);

        Assert.Equal(25, list.End);
    }

    [Fact]
    public void UnmeasuredBlocksAboveTheViewAreMeasuredOutOfSight()
    {
        var list = new VirtualList();
        for (int i = 0; i < 100; i++)
        {
            list.Add(50f, known: i >= 20);
        }
        list.Arrange(scroll: 1000f, view: 500f, below: 0f, above: 120f, minHeight: 20f, spare: 0);

        Assert.Equal(20, list.First);
        // Blocks 17 to 19 reach into the 120 pixels above the view, and none of them is drawn in the list.
        Assert.Equal(new[] { 19, 18, 17 }, list.Offscreen);
        Assert.Equal(1000f, list.TopSpace);
    }

    [Fact]
    public void TheViewHoldsStillWhenEstimatesAboveItAreCorrected()
    {
        // Every block is estimated at 50 but really 80. The menu remembers the block at the top of
        // the view and how far into it, rebuilds with the measured heights, and scrolls back to it.
        var list = Rows(200, 50f, known: false);
        float scroll = 3010f;
        list.Arrange(scroll, 500f, 0f, 400f, 20f, 0);
        var measured = new HashSet<int>(list.Offscreen);
        Assert.NotEmpty(measured);

        int anchor = list.IndexAt(scroll);
        float offset = scroll - list.DisplayedTop(anchor);
        float onScreen = list.DisplayedTop(anchor + 1) - scroll;

        var next = new VirtualList();
        for (int i = 0; i < 200; i++)
        {
            next.Add(measured.Contains(i) ? 80f : 50f, measured.Contains(i));
        }
        scroll = next.Y(anchor) + offset;
        next.Arrange(scroll, 500f, 0f, 400f, 20f, 0);

        Assert.Equal(anchor, next.First);
        Assert.Equal(onScreen, next.DisplayedTop(anchor + 1) - scroll, 3);
    }

    [Fact]
    public void DrawnBlocksAreFoundWhereTheyReallyLanded()
    {
        // Blocks 10 to 19 are drawn; estimated at 50, they measure 70, so everything after them
        // sits 200 lower than the arrangement put it.
        var list = Rows(100, 50f, known: false);
        list.Arrange(scroll: 500f, view: 500f, below: 0f, above: 0f, minHeight: 50f, spare: 0);
        Assert.Equal(10, list.First);
        Assert.Equal(20, list.End);
        for (int i = list.First; i < list.End; i++)
        {
            list.Shown(i, 500f + (i - 10) * 70f, 70f);
        }

        Assert.Equal(450f, list.DisplayedTop(9));
        Assert.Equal(570f, list.DisplayedTop(11));
        Assert.Equal(1200f, list.DisplayedTop(20));
        Assert.Equal(11, list.IndexAt(600f));
        Assert.Equal(20, list.IndexAt(1200f));
        Assert.Equal(19, list.IndexAt(1199f));
        Assert.Equal(0, list.IndexAt(-5f));
    }

    [Fact]
    public void ArrangingWithoutDrawingShowsBlocksWhereTheEstimatesPutThem()
    {
        var list = Rows(50, 40f, known: false);
        list.Arrange(scroll: 400f, view: 200f, below: 0f, above: 0f, minHeight: 40f, spare: 0);

        for (int i = 0; i < 50; i++)
        {
            Assert.Equal(i * 40f, list.DisplayedTop(i));
        }
        Assert.Equal(10, list.IndexAt(400f));
    }

    [Fact]
    public void SpareMeasuringTakesTheNearestUnmeasuredBlocksBelowFirst()
    {
        var list = Rows(100, 50f, known: false);
        list.Arrange(scroll: 0f, view: 500f, below: 0f, above: 0f, minHeight: 50f, spare: 3);

        Assert.Equal(10, list.End);
        Assert.Equal(new[] { 10, 11, 12 }, list.Offscreen);
    }

    [Fact]
    public void AnEmptyListDrawsNothing()
    {
        var list = new VirtualList();
        list.Arrange(0f, 500f, 100f, 100f, 20f, 4);

        Assert.Equal(0, list.First);
        Assert.Equal(0, list.End);
        Assert.Equal(0f, list.TopSpace);
        Assert.Equal(0f, list.BottomSpace);
        Assert.Empty(list.Offscreen);
        Assert.Equal(-1, list.IndexAt(0f));
    }

    [Fact]
    public void ClearKeepsCapacityButForgetsBlocks()
    {
        var list = Rows(600, 10f);
        list.Clear();
        list.Add(25f, true);

        Assert.Equal(1, list.Count);
        Assert.Equal(25f, list.Total);
        Assert.Equal(0f, list.Y(0));
    }
}
