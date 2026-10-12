using System.Windows;
using Memoit.Models;
using Memoit.Services;
using Xunit;

namespace Memoit.Tests;

public sealed class ExpandedPagingTests
{
    [Theory]
    [InlineData(ExpandedCorner.TopLeft, 1)]
    [InlineData(ExpandedCorner.TopRight, 1.5)]
    [InlineData(ExpandedCorner.BottomLeft, 2)]
    [InlineData(ExpandedCorner.BottomRight, 1)]
    public void EveryNoteAppearsOnceInOrderWithoutOverlap(ExpandedCorner corner, double scale)
    {
        var notes = Enumerable.Range(0, 25).Select(i => new Note
        {
            Width = 100 + i % 3 * 15, Height = 80 + i % 4 * 10,
            CreatedAt = DateTimeOffset.UnixEpoch.AddSeconds(i)
        }).ToArray();
        var area = new Rect(-1000, -500, 280 * scale, 240 * scale);
        var order = notes.Reverse().Select(n => n.Id).ToArray();
        var pages = NoteArrangement.ArrangeExpandedPages(notes, area, new MonitorLayout(), corner, scale, orderedIds: order);
        Assert.True(pages.Count > 1);
        Assert.Equal(order, pages.SelectMany(p => p.Keys));
        foreach (var page in pages)
        {
            Assert.NotEmpty(page);
            var bounds = page.Values.ToArray();
            for (int i = 0; i < bounds.Length; i++)
            {
                Assert.True(area.Contains(bounds[i]));
                for (int j = i + 1; j < bounds.Length; j++)
                    Assert.False(bounds[i].IntersectsWith(bounds[j]));
            }
        }
    }

    [Fact]
    public void ExactFitStaysTogetherAndNextNoteStartsAnotherPage()
    {
        var notes = Enumerable.Range(0, 5).Select(i => new Note { Width = 100, Height = 80, CreatedAt = DateTimeOffset.UnixEpoch.AddSeconds(i) }).ToArray();
        var pages = NoteArrangement.ArrangeExpandedPages(notes, new Rect(0, 0, 224, 184), new MonitorLayout());
        Assert.Equal(new[] { 4, 1 }, pages.Select(p => p.Count));
        var first = NoteArrangement.ArrangeExpanded(notes, new Rect(0, 0, 224, 184), new MonitorLayout());
        Assert.Equal(pages[0], first.Bounds);
        Assert.Equal(1, first.SkippedTiles);
    }

    [Fact]
    public void OversizedNotesAreClampedAndEachGetsAFullPage()
    {
        var notes = new[] { new Note { Width = 4000, Height = 3000 }, new Note { Width = 5000, Height = 2000 } };
        var pages = NoteArrangement.ArrangeExpandedPages(notes, new Rect(-500, -300, 300, 200), new MonitorLayout());
        Assert.Equal(2, pages.Count);
        Assert.All(pages, p => Assert.Equal(new Rect(-492, -292, 284, 184), Assert.Single(p).Value));
    }

    [Fact]
    public void EmptyHiddenDeletedAndCollapsedTargetsProduceNoPages()
    {
        Assert.Empty(NoteArrangement.ArrangeExpandedPages([], new Rect(0, 0, 800, 600), new MonitorLayout()));
        Assert.Empty(NoteArrangement.ArrangeExpandedPages(
            [new Note { IsVisible = false }, new Note { DeletedAt = DateTimeOffset.UtcNow }, new Note { IsCollapsed = true }],
            new Rect(0, 0, 800, 600), new MonitorLayout()));
    }
}
