using CombatMeter.Client;
using Xunit;

namespace CombatMeter.Tests;

public sealed class ScrollGeometryTests
{
    [Fact]
    public void HistoryThumbTracksWheelOffsetsAndDraggingWithoutJumping()
    {
        var geometry = ScrollGeometry.Calculate(20 * 36, 252, 234);
        Assert.True(geometry.CanScroll);
        Assert.Equal(468, geometry.Range);
        Assert.Equal(88.2f, geometry.ThumbHeight, 3);
        Assert.Equal(geometry.Travel / 2, geometry.ThumbTop, 3);
        Assert.Equal(234, geometry.OffsetAt(geometry.ThumbTop + 12, 12), 3);
        Assert.Equal(0, geometry.OffsetAt(-100, 12));
        Assert.Equal(468, geometry.OffsetAt(1000, 12));
    }

    [Fact]
    public void SourceOverflowKeepsAGrabbableThumbAndReachesLastSource()
    {
        var geometry = ScrollGeometry.Calculate(130 * 30, 270, 0);
        Assert.Equal(24, geometry.ThumbHeight);
        Assert.Equal(0, geometry.ThumbTop);
        Assert.Equal(3630, geometry.OffsetAt(geometry.Travel + 10, 10));
        var bottom = ScrollGeometry.Calculate(130 * 30, 270, 3630);
        Assert.Equal(270, bottom.ThumbTop + bottom.ThumbHeight);
    }

    [Fact]
    public void FittingEmptyOrTinyListsHaveNoInvalidDragRange()
    {
        foreach (var (content, height) in new[] { (0f, 252f), (252f, 252f), (36f, 252f), (100f, 0f) })
        {
            var geometry = ScrollGeometry.Calculate(content, height, 1000);
            Assert.False(geometry.CanScroll);
            Assert.Equal(0, geometry.OffsetAt(100, 12));
        }
        var tiny = ScrollGeometry.Calculate(1000, 12, 900);
        Assert.Equal(12, tiny.ThumbHeight);
        Assert.True(float.IsFinite(tiny.OffsetAt(10, 1)));
    }

    [Fact]
    public void HistoryEvictionAndViewportResizingClampPreviousScrollPositions()
    {
        var oldBottom = ScrollGeometry.Calculate(756, 252, 504);
        var shorter = ScrollGeometry.Calculate(360, 252, oldBottom.Range);
        Assert.Equal(shorter.Travel, shorter.ThumbTop);
        Assert.Equal(108, shorter.OffsetAt(shorter.ThumbTop + 8, 8));
        var expanded = ScrollGeometry.Calculate(360, 400, 108);
        Assert.False(expanded.CanScroll);
        Assert.Equal(0, expanded.ThumbTop);
    }
}
