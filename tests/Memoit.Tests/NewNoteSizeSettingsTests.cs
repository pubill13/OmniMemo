using Memoit.Services;
using Xunit;

namespace Memoit.Tests;

public sealed class NewNoteSizeSettingsTests
{
    [Theory]
    [InlineData(199, 300)]
    [InlineData(320, 149)]
    [InlineData(double.NaN, 300)]
    [InlineData(320, double.PositiveInfinity)]
    public void InvalidSizeIsRejected(double width, double height)
        => Assert.Throws<InvalidDataException>(() => new LayoutSettings { NewNoteWidth = width, NewNoteHeight = height }.Validate());

    [Fact]
    public void LegacySettingsUseOriginalSizeAndCustomSizeSurvivesRestart()
    {
        string path = Path.Combine(Path.GetTempPath(), "omnimemo-size-" + Guid.NewGuid() + ".json");
        try
        {
            File.WriteAllText(path, "{\"Version\":6}");
            var old = LayoutSettings.Load(path);
            Assert.Equal(320, old.NewNoteWidth); Assert.Equal(300, old.NewNoteHeight);
            var custom = old with { NewNoteWidth = 460.5, NewNoteHeight = 220 };
            custom.Save(path);
            var loaded = LayoutSettings.Load(path);
            Assert.Equal(custom.NewNoteWidth, loaded.NewNoteWidth);
            Assert.Equal(custom.NewNoteHeight, loaded.NewNoteHeight);
            Assert.Throws<InvalidDataException>(() => (custom with { NewNoteWidth = 0 }).Save(path));
            Assert.Equal(460.5, LayoutSettings.Load(path).NewNoteWidth);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
