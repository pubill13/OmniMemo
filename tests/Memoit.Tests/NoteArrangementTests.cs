using System.Windows;
using Memoit.Models;
using Memoit.Services;
using Xunit;

namespace Memoit.Tests;

public sealed class NoteArrangementTests
{
    private static Note[] Notes(int count, bool collapsed = false) => Enumerable.Range(0, count)
        .Select(i => new Note { IsCollapsed = collapsed, CreatedAt = DateTimeOffset.UnixEpoch.AddSeconds(i), Width = 100, Height = 80 }).ToArray();

    [Theory]
    [InlineData(ExpandedCorner.TopLeft, 1)]
    [InlineData(ExpandedCorner.TopRight, 1.5)]
    [InlineData(ExpandedCorner.BottomLeft, 2)]
    [InlineData(ExpandedCorner.BottomRight, 1)]
    public void ExpandedUsesCornerAndActualSizesIgnoringTileOrigin(ExpandedCorner corner, double scale)
    {
        var notes = Notes(3);
        var area = new Rect(-1500, -700, 250 * scale, 260 * scale);
        var sizes = new Dictionary<Guid, Size> { [notes[0].Id] = new(100 * scale, 80 * scale),
            [notes[1].Id] = new(110 * scale, 120 * scale), [notes[2].Id] = new(130 * scale, 70 * scale) };
        var result = NoteArrangement.ArrangeExpanded(notes, area,
            new MonitorLayout { X = 1000, Y = 1000, Columns = 1, Shape = LayoutShape.Vertical }, corner, scale, sizes);
        Assert.False(result.UsedCascade);
        var bounds = notes.Select(n => result.Bounds[n.Id]).ToArray();
        Assert.All(bounds, b => Assert.True(area.Contains(b)));
        for (int i = 0; i < bounds.Length; i++)
            for (int j = i + 1; j < bounds.Length; j++) Assert.False(bounds[i].IntersectsWith(bounds[j]));
        Assert.Equal(sizes[notes[0].Id], bounds[0].Size);
        bool right = corner is ExpandedCorner.TopRight or ExpandedCorner.BottomRight;
        bool bottom = corner is ExpandedCorner.BottomLeft or ExpandedCorner.BottomRight;
        Assert.Equal(8 * scale, right ? area.Right - bounds[0].Right : bounds[0].Left - area.Left, 6);
        Assert.Equal(8 * scale, bottom ? area.Bottom - bounds[0].Bottom : bounds[0].Top - area.Top, 6);
        // The third item may wrap depending on the actual widths; bounds and non-overlap
        // above are the contract, while the corner anchor is asserted independently.
    }

    [Theory]
    [InlineData(ExpandedCorner.TopLeft)]
    [InlineData(ExpandedCorner.TopRight)]
    [InlineData(ExpandedCorner.BottomLeft)]
    [InlineData(ExpandedCorner.BottomRight)]
    public void OverflowCascadesWholeGroupAndRestartsAtEdge(ExpandedCorner corner)
    {
        var notes = Notes(5);
        var area = new Rect(-300, -100, 160, 140);
        var result = NoteArrangement.ArrangeExpanded(notes, area, new MonitorLayout(), corner);
        Assert.True(result.UsedCascade);
        Assert.Equal(5, result.Bounds.Count);
        Assert.All(result.Bounds.Values, b => Assert.True(area.Contains(b)));
        var first = result.Bounds[notes[0].Id];
        var second = result.Bounds[notes[1].Id];
        Assert.Equal(24, Math.Abs(first.X - second.X));
        Assert.Equal(24, Math.Abs(first.Y - second.Y));
        Assert.Equal(first, result.Bounds[notes[2].Id]);
    }

    [Fact]
    public void OversizeIsClampedToWorkArea()
    {
        var note = new Note { Width = 2000, Height = 1000 };
        var area = new Rect(-500, -300, 300, 200);
        var result = NoteArrangement.ArrangeExpanded([note], area, new MonitorLayout());
        Assert.Equal(area, result.Bounds[note.Id]);
        Assert.True(result.UsedCascade);
    }

    [Theory]
    [InlineData(LayoutShape.Horizontal, 2)]
    [InlineData(LayoutShape.Vertical, 2)]
    [InlineData(LayoutShape.Grid, 4)]
    public void TilesKeepShapeAndOnlyReturnFittingItems(LayoutShape shape, int expected)
    {
        var notes = Notes(6, true);
        var result = NoteArrangement.ArrangeCollapsed(notes, new Rect(-100, -100, 96, 96), new MonitorLayout { Shape = shape });
        Assert.Equal(expected, result.Bounds.Count);
        Assert.Equal(6 - expected, result.SkippedTiles);
        Assert.Equal(new Point(-92, -92), result.Bounds[notes[0].Id].Location);
        Assert.False(result.UsedCascade);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(1.5)]
    [InlineData(2)]
    public void TileOriginGapAndColumnsScale(double scale)
    {
        var notes = Notes(3, true);
        var result = NoteArrangement.ArrangeCollapsed(notes, new Rect(-1500, -800, 1000, 1000),
            new MonitorLayout { X = 20, Y = 30, Gap = 10, Columns = 2 }, scale);
        Assert.Equal(new Point(-1500 + 20 * scale, -800 + 30 * scale), result.Bounds[notes[0].Id].Location);
        Assert.Equal(new Point(-1500 + 66 * scale, -800 + 30 * scale), result.Bounds[notes[1].Id].Location);
        Assert.Equal(new Point(-1500 + 20 * scale, -800 + 76 * scale), result.Bounds[notes[2].Id].Location);
    }

    [Fact]
    public void OnlyVisibleMatchingStateNonTrashNotesAreArrangedAndTitlesHaveStableTies()
    {
        var early = new Note { Body = "가나다", Id = Guid.Parse("00000000-0000-0000-0000-000000000001"), CreatedAt = DateTimeOffset.UnixEpoch };
        var late = early with { Id = Guid.Parse("00000000-0000-0000-0000-000000000002") };
        var last = early with { Id = Guid.NewGuid(), Body = "하늘" };
        Note[] notes = [last, late, early, early with { Id = Guid.NewGuid(), IsVisible = false },
            early with { Id = Guid.NewGuid(), DeletedAt = DateTimeOffset.UtcNow }, early with { Id = Guid.NewGuid(), IsCollapsed = true }];
        var result = NoteArrangement.ArrangeExpanded(notes, new Rect(0, 0, 1600, 900), new MonitorLayout { Sort = LayoutSort.Title }, ExpandedCorner.TopLeft);
        Assert.Equal(3, result.Bounds.Count);
        Assert.True(result.Bounds[early.Id].X < result.Bounds[late.Id].X);
        Assert.True(result.Bounds[late.Id].X < result.Bounds[last.Id].X);
    }
}
