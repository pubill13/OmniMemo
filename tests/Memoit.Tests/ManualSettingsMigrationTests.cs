using Memoit.Services;
using Xunit;

namespace Memoit.Tests;

public sealed class ManualSettingsMigrationTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void UpgradeDisablesAutomationAndDoesNotReuseItsShortcut(int version)
    {
        var file = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            File.WriteAllText(file, "{\"Version\":" + version + ",\"AutoArrange\":true,\"Hotkeys\":{\"ToggleAuto\":\"Ctrl+Alt+Shift+Z\",\"Arrange\":\"Ctrl+Alt+Shift+G\"},\"Monitors\":{\"old\":{\"X\":120,\"Columns\":3}}}");
            var loaded = LayoutSettings.Load(file);
            Assert.Equal(5, loaded.Version);
            Assert.False(loaded.AutoArrange);
            Assert.True(loaded.NeedsManualArrangementNotice);
            Assert.DoesNotContain("ToggleAuto", loaded.Hotkeys.Keys);
            Assert.DoesNotContain("Ctrl+Alt+Shift+Z", loaded.Hotkeys.Values);
            Assert.DoesNotContain("Arrange", loaded.Hotkeys.Keys);
            Assert.DoesNotContain("Ctrl+Alt+Shift+G", loaded.Hotkeys.Values);
            Assert.Equal(120, loaded.GetMonitor("old").X);
            Assert.Equal(3, loaded.GetMonitor("old").Columns);
            Assert.Equal(ExpandedCorner.TopRight, loaded.GetMonitor("old").ExpandedCorner);
            (loaded with { NeedsManualArrangementNotice = false }).Save(file);
            Assert.False(LayoutSettings.Load(file).NeedsManualArrangementNotice);
            HotkeyService.Validate(loaded.Hotkeys);
        }
        finally { File.Delete(file); File.Delete(file + ".pre-v5.bak"); }
    }
}
