using System.Windows;
using Memoit.Models;
using Memoit.Services;
using Xunit;

namespace Memoit.Tests;

public sealed class ArrangementOrderTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void UpgradeRemovesRetiredCommandsWithoutReassigningTheirKeys(int version)
    {
        WithFile(file =>
        {
            File.WriteAllText(file, "{\"Version\":" + version + ",\"Hotkeys\":{\"ArrangeCollapsed\":\"Ctrl+Alt+Shift+R\",\"ArrangeExpanded\":\"Ctrl+Alt+Shift+X\",\"UndoArrange\":\"Ctrl+Alt+Shift+Y\",\"CollapseAll\":\"Ctrl+Alt+Shift+G\"}}");
            var settings = LayoutSettings.Load(file);
            Assert.Equal(5, settings.Version);
            Assert.Empty(settings.ArrangementOrder);
            Assert.DoesNotContain("ArrangeCollapsed", settings.Hotkeys.Keys);
            Assert.DoesNotContain("ArrangeExpanded", settings.Hotkeys.Keys);
            Assert.DoesNotContain("UndoArrange", settings.Hotkeys.Keys);
            Assert.DoesNotContain("Arrange", settings.Hotkeys.Keys);
            Assert.DoesNotContain("CollapseAll", settings.Hotkeys.Keys);
            Assert.DoesNotContain("Ctrl+Alt+Shift+G", settings.Hotkeys.Values);
            HotkeyService.Validate(settings.Hotkeys);
        });
    }

    [Fact]
    public void PerMonitorOrderSurvivesRestartIncludingDisconnectedMonitor()
    {
        WithFile(file =>
        {
            var first = Guid.NewGuid(); var second = Guid.NewGuid();
            new LayoutSettings { ArrangementOrder = new() { ["left"] = [second, first], ["disconnected"] = [first] } }.Save(file);
            var loaded = LayoutSettings.Load(file);
            Assert.Equal(new[] { second, first }, loaded.ArrangementOrder["left"]);
            Assert.Equal(new[] { first }, loaded.ArrangementOrder["disconnected"]);
        });
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{\"monitor\":null}")]
    [InlineData("{\"monitor\":[\"00000000-0000-0000-0000-000000000000\"]}")]
    [InlineData("{\"monitor\":[\"00000000-0000-0000-0000-000000000001\",\"00000000-0000-0000-0000-000000000001\"]}")]
    [InlineData("{\"\":[]}")]
    public void InvalidSavedOrderIsRejected(string order)
        => WithFile(file =>
        {
            File.WriteAllText(file, "{\"Version\":4,\"ArrangementOrder\":" + order + "}");
            Assert.Throws<InvalidDataException>(() => LayoutSettings.Load(file));
        });

    [Fact]
    public void ExpandedUsesSavedOrderThenCurrentSortForNewNotes()
    {
        var first = new Note { Body = "하늘", Width = 100, Height = 80 };
        var second = first with { Id = Guid.NewGuid(), Body = "가나다" };
        var addedLate = first with { Id = Guid.NewGuid(), Body = "다람쥐" };
        var addedEarly = first with { Id = Guid.NewGuid(), Body = "나무" };
        var result = NoteArrangement.ArrangeExpanded([addedLate, second, addedEarly, first], new Rect(0, 0, 1000, 600),
            new MonitorLayout { Sort = LayoutSort.Title }, ExpandedCorner.TopLeft,
            orderedIds: [Guid.NewGuid(), first.Id, second.Id]);
        Assert.Equal(new[] { first.Id, second.Id, addedEarly.Id, addedLate.Id }, result.Bounds.Keys);
    }

    [Fact]
    public void PublicSortIncludesBothStatesButExcludesHiddenAndTrash()
    {
        var first = new Note { CreatedAt = DateTimeOffset.UnixEpoch, IsCollapsed = true };
        var second = new Note { CreatedAt = DateTimeOffset.UnixEpoch.AddSeconds(1) };
        var sorted = NoteArrangement.OrderNotes([second, first, new Note { IsVisible = false }, new Note { DeletedAt = DateTimeOffset.UtcNow }], LayoutSort.Created);
        Assert.Equal(new[] { first.Id, second.Id }, sorted.Select(n => n.Id));
    }

    private static void WithFile(Action<string> test)
    {
        var file = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try { test(file); }
        finally { File.Delete(file); File.Delete(file + ".pre-v5.bak"); }
    }
}
