using System.Windows;
using Memoit.Services;
using Xunit;

namespace Memoit.Tests;

public sealed class ColorNotePlacementTests
{
    private static readonly MonitorDescriptor Monitor = new("m", "m", new Rect(0, 0, 1000, 800), 1);
    private static readonly MonitorLayout Options = new() { X = 80, Y = 90, Gap = 8 };

    [Fact]
    public void ExpandedNewNoteStartsBesideLastTileWithoutMovingPeers()
    {
        Rect[] peers = [new(80, 90, 36, 36), new(124, 90, 36, 36)];
        var snapshot = peers.ToArray();
        var result = NotePlacement.CreateBesideColor(new Size(320, 300), Monitor, Options, peers, peers);
        Assert.Equal(new Rect(168, 90, 320, 300), result.Bounds);
        Assert.Equal(new Point(168, 90), result.CollapsedPosition);
        Assert.False(result.Overlaps);
        Assert.Equal(snapshot, peers);
    }

    [Fact]
    public void LastMeansCallerOrderRatherThanGeometricallyRightmost()
    {
        Rect[] peers = [new(500, 90, 36, 36), new(80, 90, 36, 36)];
        var result = NotePlacement.CreateBesideColor(new Size(320, 300), Monitor, Options, peers, peers);
        Assert.Equal(new Point(124, 90), result.Bounds.Location);
    }

    [Fact]
    public void ExpandedFootprintWrapsWhileCollapsedAnchorStaysBesideTile()
    {
        Rect[] peers = [new(80, 90, 36, 36), new(900, 90, 36, 36)];
        var result = NotePlacement.CreateBesideColor(new Size(320, 300), Monitor, Options, peers, peers);
        Assert.Equal(new Point(80, 134), result.Bounds.Location);
        Assert.Equal(new Point(944, 90), result.CollapsedPosition);
    }

    [Fact]
    public void OtherColorsAreObstaclesAndFreePlacementAvoidsThem()
    {
        Rect[] group = [new(80, 90, 36, 36)];
        Rect[] peers = [group[0], new(124, 90, 320, 300), new(80, 134, 36, 36)];
        var result = NotePlacement.CreateBesideColor(new Size(320, 300), Monitor, Options, group, peers);
        Assert.False(result.Overlaps);
        Assert.True(Monitor.WorkArea.Contains(result.Bounds));
        Assert.NotEqual(new Point(124, 90), result.Bounds.Location);
    }

    [Fact]
    public void AbsentColorStartsAtConfiguredTileOrigin()
    {
        var result = NotePlacement.CreateBesideColor(new Size(320, 300), Monitor, Options, [], []);
        Assert.Equal(new Point(80, 90), result.Bounds.Location);
    }

    [Fact]
    public void FullScreenFallsBackInsideAreaAndReportsOverlap()
    {
        Rect[] peers = [Monitor.WorkArea];
        var result = NotePlacement.CreateBesideColor(new Size(320, 300), Monitor, Options, peers, peers);
        Assert.Equal(new Point(24, 24), result.Bounds.Location);
        Assert.True(result.Overlaps);
        Assert.True(Monitor.WorkArea.Contains(result.Bounds));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(1.5)]
    [InlineData(2)]
    public void NegativeMonitorCoordinatesAndDpiUsePhysicalBounds(double scale)
    {
        var monitor = new MonitorDescriptor("m", "m", new Rect(-2000, -500, 1000 * scale, 800 * scale), scale);
        Rect[] group = [new(-2000 + 80 * scale, -500 + 90 * scale, 36 * scale, 36 * scale)];
        var result = NotePlacement.CreateBesideColor(new Size(320, 300), monitor, Options, group, group);
        Assert.Equal(new Rect(-2000 + 124 * scale, -500 + 90 * scale, 320 * scale, 300 * scale), result.Bounds);
    }

    [Fact]
    public void OversizedNewNoteIsLimitedToWorkArea()
    {
        var result = NotePlacement.CreateBesideColor(new Size(2000, 2000), Monitor, Options, [], []);
        Assert.Equal(Monitor.WorkArea, result.Bounds);
    }
}
