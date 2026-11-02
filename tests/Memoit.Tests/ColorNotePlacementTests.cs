using System.Windows;
using Memoit.Services;
using Xunit;

namespace Memoit.Tests;

public sealed class TileOriginNotePlacementTests
{
    private static readonly MonitorDescriptor Monitor = new("m", "m", new Rect(0, 0, 1000, 800), 1);
    private static readonly MonitorLayout Options = new() { X = 80, Y = 90, Gap = 8 };

    [Fact]
    public void RightTileOriginStaysNearRightEdgeInsteadOfSearchingLeft()
    {
        var result = NotePlacement.CreateAtTileOrigin(new Size(320, 300), Monitor,
            Options with { X = 950 }, []);
        Assert.Equal(new Rect(680, 90, 320, 300), result);
    }

    [Fact]
    public void OverlapIsAllowedWithoutSearchingForFreeSpaceOrMovingPeers()
    {
        Rect[] peers = [new(50, 60, 600, 500)];
        var snapshot = peers.ToArray();
        var result = NotePlacement.CreateAtTileOrigin(new Size(320, 300), Monitor, Options, peers);
        Assert.Equal(new Point(80, 90), result.Location);
        Assert.True(result.IntersectsWith(peers[0]));
        Assert.Equal(snapshot, peers);
    }

    [Fact]
    public void ConsecutiveNotesCascadeBy24DipFromConfiguredOrigin()
    {
        var peers = new List<Rect>();
        for (int i = 0; i < 5; i++)
        {
            var result = NotePlacement.CreateAtTileOrigin(new Size(320, 300), Monitor, Options, peers);
            Assert.Equal(new Point(80 + 24 * i, 90 + 24 * i), result.Location);
            peers.Add(result);
        }
    }

    [Theory]
    [InlineData(950, 90, -24, 24)]
    [InlineData(80, 750, 24, -24)]
    [InlineData(950, 750, -24, -24)]
    public void EdgeOriginCascadesTowardScreenInterior(double x, double y, double dx, double dy)
    {
        var options = Options with { X = x, Y = y };
        var first = NotePlacement.CreateAtTileOrigin(new Size(320, 300), Monitor, options, []);
        var second = NotePlacement.CreateAtTileOrigin(new Size(320, 300), Monitor, options, [first]);
        Assert.Equal(first.Left + dx, second.Left);
        Assert.Equal(first.Top + dy, second.Top);
        Assert.True(Monitor.WorkArea.Contains(second));
    }

    [Fact]
    public void BoundaryRestartsAtBaseEvenWhenItIsOccupied()
    {
        var monitor = new MonitorDescriptor("m", "m", new Rect(0, 0, 400, 380), 1);
        var options = Options with { X = 0, Y = 0 };
        var peers = Enumerable.Range(0, 4).Select(i => new Rect(24 * i, 24 * i, 320, 300)).ToArray();
        Assert.Equal(peers[0], NotePlacement.CreateAtTileOrigin(new Size(320, 300), monitor, options, peers));
        Assert.Equal(peers[1], NotePlacement.CreateAtTileOrigin(new Size(320, 300), monitor, options, [.. peers, peers[0]]));
    }

    [Fact]
    public void MissingIntermediatePositionIsReused()
    {
        Rect[] peers = [new(80, 90, 320, 300), new(128, 138, 320, 300)];
        var result = NotePlacement.CreateAtTileOrigin(new Size(320, 300), Monitor, Options, peers);
        Assert.Equal(new Point(104, 114), result.Location);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(1.5)]
    [InlineData(2)]
    public void NegativeMonitorCoordinatesAndDpiUsePhysicalBounds(double scale)
    {
        var monitor = new MonitorDescriptor("m", "m", new Rect(-2000, -500, 1000 * scale, 800 * scale), scale);
        var options = Options with { X = 950 };
        var first = NotePlacement.CreateAtTileOrigin(new Size(320, 300), monitor, options, []);
        var second = NotePlacement.CreateAtTileOrigin(new Size(320, 300), monitor, options, [first]);
        Assert.Equal(new Rect(-2000 + 680 * scale, -500 + 90 * scale, 320 * scale, 300 * scale), first);
        Assert.Equal(new Point(first.Left - 24 * scale, first.Top + 24 * scale), second.Location);
    }

    [Fact]
    public void OversizedNoteFitsSmallWorkAreaAndCannotCascadeOutside()
    {
        var monitor = new MonitorDescriptor("m", "m", new Rect(-50, 20, 200, 180), 1);
        var first = NotePlacement.CreateAtTileOrigin(new Size(320, 300), monitor, Options, []);
        Assert.Equal(monitor.WorkArea, first);
        Assert.Equal(first, NotePlacement.CreateAtTileOrigin(new Size(320, 300), monitor, Options, [first]));
    }
}
