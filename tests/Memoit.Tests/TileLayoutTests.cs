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
            Assert.Equal(new LayoutSettings(), LayoutSettings.Load(file));
            var settings = new LayoutSettings { AutoArrange = true, SortByColor = true };
            settings.Save(file);
            Assert.Equal(settings, LayoutSettings.Load(file));
            new LayoutSettings().Save(file);
            Assert.Equal(new LayoutSettings(), LayoutSettings.Load(file));
            File.WriteAllText(file, "{\"AutoArrange\":\"yes\"}");
            Assert.Throws<System.Text.Json.JsonException>(() => LayoutSettings.Load(file));
            File.WriteAllText(file, "{\"AutoArrange\":true,\"AutoArrange\":false}");
            Assert.Throws<InvalidDataException>(() => LayoutSettings.Load(file));
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }
}
