using System.Windows;
using Memoit.Models;
using Memoit.Services;
using Xunit;

namespace Memoit.Tests;

public sealed class ArrangementHistoryTests
{
    private static MonitorDescriptor Monitor(string id, Rect area)
        => new(id, id, area, 1);

    [Fact]
    public void RestoresOnlyCurrentVisibleNotesWithSameCollapseState()
    {
        var keep = new Note { Id = Guid.NewGuid() };
        var collapsed = new Note { Id = Guid.NewGuid(), IsCollapsed = true };
        var hidden = new Note { Id = Guid.NewGuid(), IsVisible = false };
        var deleted = new Note { Id = Guid.NewGuid(), DeletedAt = DateTimeOffset.UtcNow };
        var history = new ArrangementHistory();
        history.Capture([
            new(keep.Id, false, new Rect(10, 20, 100, 80), "a"),
            new(collapsed.Id, false, new Rect(20, 30, 100, 80), "a"),
            new(hidden.Id, false, new Rect(20, 30, 100, 80), "a"),
            new(deleted.Id, false, new Rect(20, 30, 100, 80), "a")]);
        var restored = history.GetRestorable([keep, collapsed, hidden, deleted], [Monitor("a", new Rect(0, 0, 500, 500))]);
        Assert.Equal(new Rect(10, 20, 100, 80), restored[keep.Id]);
        Assert.DoesNotContain(collapsed.Id, restored.Keys);
        Assert.DoesNotContain(hidden.Id, restored.Keys);
        Assert.DoesNotContain(deleted.Id, restored.Keys);
    }

    [Fact]
    public void MissingMonitorClampsToRemainingWorkAreaAndOversize()
    {
        var note = new Note { Id = Guid.NewGuid() };
        var history = new ArrangementHistory();
        history.Capture([new(note.Id, false, new Rect(-1000, -800, 900, 700), "gone")]);
        var restored = history.GetRestorable([note], [Monitor("remaining", new Rect(-500, -300, 400, 250))]);
        Assert.Equal(new Rect(-500, -300, 400, 250), restored[note.Id]);
    }

    [Fact]
    public void ClearMakesUndoOneShotAndRecaptureReplacesSnapshot()
    {
        var note = new Note();
        var history = new ArrangementHistory();
        history.Capture([new(note.Id, false, new Rect(1, 2, 3, 4), "a")]);
        Assert.Single(history.GetRestorable([note], [Monitor("a", new Rect(0, 0, 100, 100))]));
        history.Capture([new(note.Id, false, new Rect(20, 30, 3, 4), "a")]);
        Assert.Equal(new Rect(20, 30, 3, 4), history.GetRestorable([note], [Monitor("a", new Rect(0, 0, 100, 100))])[note.Id]);
        history.Clear();
        Assert.Empty(history.GetRestorable([note], [Monitor("a", new Rect(0, 0, 100, 100))]));
    }
}
