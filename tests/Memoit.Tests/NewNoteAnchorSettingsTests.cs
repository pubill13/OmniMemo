using System.Text.Json;
using Memoit.Services;
using Xunit;

namespace Memoit.Tests;

public sealed class NewNoteAnchorSettingsTests
{
    [Theory]
    [InlineData(null, 2d)]
    [InlineData(2d, null)]
    [InlineData(-1d, 2d)]
    [InlineData(2d, -1d)]
    [InlineData(double.NaN, 2d)]
    [InlineData(2d, double.PositiveInfinity)]
    public void InvalidOrIncompleteAnchorIsRejected(double? x, double? y)
        => Assert.Throws<InvalidDataException>(() => new MonitorLayout { NewNoteX = x, NewNoteY = y }.Validate());

    [Fact]
    public void OldConfigurationDefaultsToTileOriginAndExplicitOriginRoundTrips()
    {
        var old = JsonSerializer.Deserialize<MonitorLayout>("{\"X\":950,\"Y\":30}")!;
        old.Validate();
        Assert.Null(old.NewNoteX); Assert.Null(old.NewNoteY);
        var path = Path.Combine(Path.GetTempPath(), "omnimemo-anchor-" + Guid.NewGuid() + ".json");
        try
        {
            var settings = new LayoutSettings { Monitors = new() { ["m"] = old with { NewNoteX = 125.5, NewNoteY = 70 } } };
            settings.Save(path);
            var restored = LayoutSettings.Load(path).GetMonitor("m");
            Assert.Equal(settings.GetMonitor("m"), restored);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
