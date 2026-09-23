using System.Windows;
using Memoit.Models;
using Memoit.Services;
using Xunit;

namespace Memoit.Tests;

public sealed class TileLayoutTests
{
    [Fact]
    public void ArrangesOldestFirstWithinNegativeMonitorBounds()
    {
        var first = new Note { CreatedAt = DateTimeOffset.UtcNow.AddDays(-1) };
        var second = new Note { CreatedAt = first.CreatedAt.AddHours(1) };
        var third = new Note { CreatedAt = first.CreatedAt.AddHours(2) };
        var area = new Rect(-1000, -300, 96, 96);
        var positions = TileLayout.Arrange([third, second, first], area);
        Assert.Equal(new Point(-992, -292), positions[first.Id]);
        Assert.Equal(new Point(-948, -292), positions[second.Id]);
        Assert.Equal(new Point(-992, -248), positions[third.Id]);
        Assert.All(positions.Values, p => Assert.True(area.Contains(new Rect(p, new Size(36, 36)))));
    }

    [Fact]
    public void ColorOrderingGroupsYellowBeforePinkThenCreationTime()
    {
        var pink = new Note { Color = "#FFDDE7", CreatedAt = DateTimeOffset.UtcNow.AddDays(-2) };
        var yellow = new Note { Color = "#FFF2B3", CreatedAt = DateTimeOffset.UtcNow.AddDays(-1) };
        var legacyYellow = new Note { Color = "#FFF2B2" };
        var positions = TileLayout.Arrange([pink, legacyYellow, yellow], new Rect(0, 0, 200, 100), byColor: true);
        Assert.Equal(new Point(8, 8), positions[yellow.Id]);
        Assert.Equal(new Point(52, 8), positions[legacyYellow.Id]);
        Assert.Equal(new Point(96, 8), positions[pink.Id]);
    }

    [Fact]
    public void OverflowRejectsWholeArrangement()
    {
        Assert.Throws<InvalidOperationException>(() => TileLayout.Arrange([new Note(), new Note()], new Rect(0, 0, 52, 52)));
        Assert.Throws<ArgumentException>(() => TileLayout.Arrange([], new Rect(0, 0, 52, 52), tileSize: double.NaN));
    }

    [Fact]
    public void ScaledTileAndGapKeepMatchingMargin()
    {
        var n = new Note();
        Assert.Equal(new Point(16, 16), TileLayout.Arrange([n], new Rect(0, 0, 104, 104), 72, 16)[n.Id]);
    }

    [Fact]
    public void SettingsRoundTripAndInvalidFilesSurfaceErrors()
    {
        string folder = Path.Combine(Path.GetTempPath(), "OmniLayout", Guid.NewGuid().ToString("N"));
        string file = Path.Combine(folder, "layout.json");
        try
        {
            Assert.False(LayoutSettings.Load(file).AutoArrange);
            var settings = new LayoutSettings { AutoArrange = true, SortByColor = true };
            settings.Save(file);
            Assert.True(LayoutSettings.Load(file).AutoArrange);
            Assert.True(LayoutSettings.Load(file).SortByColor);
            new LayoutSettings().Save(file);
            Assert.False(LayoutSettings.Load(file).AutoArrange);
            Assert.False(LayoutSettings.Load(file).SortByColor);
            File.WriteAllText(file, "{\"AutoArrange\":\"yes\"}");
            Assert.Throws<System.Text.Json.JsonException>(() => LayoutSettings.Load(file));
            File.WriteAllText(file, "{\"AutoArrange\":true,\"AutoArrange\":false}");
            Assert.Throws<InvalidDataException>(() => LayoutSettings.Load(file));
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(1.5)]
    [InlineData(2)]
    public void MonitorOptionsScaleOffsetsGapAndFixedColumns(double scale)
    {
        var notes = Enumerable.Range(0, 3).Select(i => new Note { CreatedAt = DateTimeOffset.UnixEpoch.AddSeconds(i) }).ToArray();
        var area = new Rect(-1200, -500, 1000, 900);
        var options = new MonitorLayout { X = 12, Y = 20, Gap = 10, Columns = 2 };
        var points = TileLayout.Arrange(notes.Reverse(), area, options, scale);
        Assert.Equal(new Point(-1200 + 12 * scale, -500 + 20 * scale), points[notes[0].Id]);
        Assert.Equal(new Point(-1200 + 58 * scale, -500 + 20 * scale), points[notes[1].Id]);
        Assert.Equal(new Point(-1200 + 12 * scale, -500 + 66 * scale), points[notes[2].Id]);
    }

    [Fact]
    public void ShapesAndOverflowAreExplicit()
    {
        var notes = Enumerable.Range(0, 3).Select(i => new Note { CreatedAt = DateTimeOffset.UnixEpoch.AddSeconds(i) }).ToArray();
        var area = new Rect(0, 0, 150, 150);
        var horizontal = TileLayout.Arrange(notes, area, new MonitorLayout { Shape = LayoutShape.Horizontal });
        Assert.Equal(new Point(96, 8), horizontal[notes[2].Id]);
        var vertical = TileLayout.Arrange(notes, area, new MonitorLayout { Shape = LayoutShape.Vertical });
        Assert.Equal(new Point(8, 96), vertical[notes[2].Id]);
        Assert.Throws<InvalidOperationException>(() => TileLayout.Arrange(notes, new Rect(0, 0, 80, 150), new MonitorLayout { Columns = 2 }));
        Assert.Throws<InvalidOperationException>(() => TileLayout.Arrange(notes, new Rect(0, 0, 80, 150), new MonitorLayout { Shape = LayoutShape.Horizontal }));
        Assert.Throws<InvalidDataException>(() => TileLayout.Arrange(notes, area, new MonitorLayout { Gap = 25 }));
    }

    [Fact]
    public void KoreanTitleOrderingUsesCreationThenIdTiebreaker()
    {
        var older = new Note { Body = "가나다", CreatedAt = DateTimeOffset.UnixEpoch };
        var newer = new Note { Body = "가나다", CreatedAt = DateTimeOffset.UnixEpoch.AddDays(1) };
        var last = new Note { Body = "하늘", CreatedAt = DateTimeOffset.UnixEpoch };
        var points = TileLayout.Arrange([last, newer, older], new Rect(0, 0, 200, 100), new MonitorLayout { Sort = LayoutSort.Title });
        Assert.Equal(new Point(8, 8), points[older.Id]);
        Assert.Equal(new Point(52, 8), points[newer.Id]);
        Assert.Equal(new Point(96, 8), points[last.Id]);
    }

    [Fact]
    public void LegacySettingsMigrateAndPerMonitorOptionsRoundTrip()
    {
        string folder = Path.Combine(Path.GetTempPath(), "OmniLayout", Guid.NewGuid().ToString("N"));
        string file = Path.Combine(folder, "layout.json");
        Directory.CreateDirectory(folder);
        try
        {
            File.WriteAllText(file, "{\"AutoArrange\":true,\"SortByColor\":true}");
            var legacy = LayoutSettings.Load(file);
            Assert.Equal(2, legacy.Version);
            Assert.Equal(LayoutSort.Color, legacy.GetMonitor("new-monitor").Sort);
            var options = new MonitorLayout { X = 18, Gap = 12, Shape = LayoutShape.Vertical, Sort = LayoutSort.Title };
            var settings = legacy with { Monitors = new() { ["monitor-a"] = options }, SelectedMonitor = "monitor-a", PanelLeft = -100, PanelTop = 50 };
            settings.Save(file);
            var loaded = LayoutSettings.Load(file);
            Assert.Equal(options, loaded.GetMonitor("monitor-a"));
            Assert.Equal(-100, loaded.PanelLeft);
            Assert.Equal("monitor-a", loaded.SelectedMonitor);
            Assert.Equal(settings.Hotkeys.OrderBy(k => k.Key), loaded.Hotkeys.OrderBy(k => k.Key));
            string original = File.ReadAllText(file);
            Assert.Throws<InvalidDataException>(() => (settings with { Monitors = new() { ["bad"] = new MonitorLayout { Columns = 31 } } }).Save(file));
            Assert.Equal(original, File.ReadAllText(file));
            File.WriteAllText(file, "{\"Version\":99}");
            Assert.Throws<InvalidDataException>(() => LayoutSettings.Load(file));
            Assert.Equal("{\"Version\":99}", File.ReadAllText(file));
        }
        finally { Directory.Delete(folder, true); }
    }
}
