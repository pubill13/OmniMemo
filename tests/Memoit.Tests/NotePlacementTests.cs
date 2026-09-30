using System.Windows;
using Memoit.Services;
using Xunit;

namespace Memoit.Tests;

public sealed class NotePlacementTests
{
    [Theory]
    [InlineData(ExpandedCorner.TopLeft, 8, 8)]
    [InlineData(ExpandedCorner.TopRight, 672, 8)]
    [InlineData(ExpandedCorner.BottomLeft, 8, 492)]
    [InlineData(ExpandedCorner.BottomRight, 672, 492)]
    public void ExternalCreationUsesConfiguredCorner(ExpandedCorner corner, double x, double y)
    {
        var monitor = new MonitorDescriptor("m", "m", new Rect(0, 0, 1000, 800), 1);
        Assert.Equal(new Rect(x, y, 320, 300), NotePlacement.Create(new Size(320, 300), monitor, corner, []));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(1.5)]
    [InlineData(2)]
    public void NegativeMonitorAndScaleKeepPhysicalOffsets(double scale)
    {
        var monitor = new MonitorDescriptor("m", "m", new Rect(-2000, -500, 1600 * scale, 1000 * scale), scale);
        var first = NotePlacement.Create(new Size(320, 300), monitor, ExpandedCorner.TopLeft, []);
        var second = NotePlacement.Create(new Size(320, 300), monitor, ExpandedCorner.TopLeft, [first]);
        Assert.Equal(-2000 + 8 * scale, first.Left);
        Assert.Equal(-500 + 8 * scale, first.Top);
        Assert.Equal(24 * scale, second.Left - first.Left);
        Assert.Equal(24 * scale, second.Top - first.Top);
        Assert.Equal(320 * scale, second.Width);
    }

    [Fact]
    public void BoundaryWrapsToOriginAndOversizedNotesFit()
    {
        var monitor = new MonitorDescriptor("m", "m", new Rect(0, 0, 340, 320), 1);
        var first = NotePlacement.Create(new Size(320, 300), monitor, ExpandedCorner.TopRight, []);
        Assert.Equal(first, NotePlacement.Create(new Size(320, 300), monitor, ExpandedCorner.TopRight, [first]));
        Assert.Equal(monitor.WorkArea, NotePlacement.Create(new Size(500, 400), monitor, ExpandedCorner.TopRight, []));
    }

    [Fact]
    public void SourceUsesItsOwnPositionAndScaledOffset()
    {
        var monitor = new MonitorDescriptor("m", "m", new Rect(0, 0, 2000, 1600), 2);
        var result = NotePlacement.Create(new Size(320, 300), monitor, ExpandedCorner.BottomRight, [], new Rect(100, 200, 640, 600));
        Assert.Equal(new Rect(156, 256, 640, 600), result);
    }

    [Fact]
    public void MonitorSelectionUsesHistoryThenCursorAfterDisconnect()
    {
        var left = new MonitorDescriptor("left", "left", new Rect(-1000, 0, 1000, 800), 1);
        var right = new MonitorDescriptor("right", "right", new Rect(0, 0, 1000, 800), 1);
        Assert.Equal(left, NotePlacement.SelectMonitor("left", new Point(300, 100), [left, right]));
        Assert.Equal(right, NotePlacement.SelectMonitor("disconnected", new Point(300, 100), [right]));
        Assert.Equal(left, NotePlacement.SelectMonitor(null, new Point(-300, 100), [left, right]));
    }

    [Fact]
    public void CascadesFromMovedSourceRatherThanFixedDesktopOrigin()
        => Assert.Equal(new Point(528, 328), NotePlacement.Cascade(new Rect(500, 300, 320, 300), new Size(320, 300), new Rect(0, 0, 1920, 1080)));

    [Fact]
    public void RightAndBottomEdgesReverseOffsetBeforeClamping()
        => Assert.Equal(new Point(652, 472), NotePlacement.Cascade(new Rect(680, 500, 320, 300), new Size(320, 300), new Rect(0, 0, 1000, 800)));

    [Fact]
    public void OnlyOverflowingAxisReverses()
        => Assert.Equal(new Point(652, 228), NotePlacement.Cascade(new Rect(680, 200, 320, 300), new Size(320, 300), new Rect(0, 0, 1000, 800)));

    [Fact]
    public void SecondaryMonitorWithNegativeCoordinatesKeepsRelativeOffset()
        => Assert.Equal(new Point(-972, -72), NotePlacement.Cascade(new Rect(-1000, -100, 320, 300), new Size(320, 300), new Rect(-1920, -200, 1920, 1080)));

    [Fact]
    public void ChildLargerThanWorkAreaClampsWithoutThrowing()
        => Assert.Equal(new Point(-200, 0), NotePlacement.Cascade(new Rect(50, 20, 36, 36), new Size(1200, 900), new Rect(-200, 0, 800, 600)));
}
