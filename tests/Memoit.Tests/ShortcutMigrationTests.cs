using Memoit.Services;
using Xunit;

namespace Memoit.Tests;

public sealed class ShortcutMigrationTests
{
    [Theory]
    [InlineData("", "Ctrl+Alt+Shift+L", "")]
    [InlineData("Ctrl+Alt+Shift+J", "Ctrl+Alt+Shift+L", "Ctrl+Alt+Shift+J")]
    [InlineData(null, "Ctrl+Alt+Shift+L", "Ctrl+Alt+Shift+L")]
    [InlineData(null, "", "")]
    public void SearchTakesPrecedenceEvenWhenExplicitlyDisabled(string? search, string showList, string expected)
        => WithFile(file =>
        {
            var keys = new Dictionary<string, string> { ["ShowList"] = showList, ["NewNote"] = "", ["ToggleCollapsed"] = "Ctrl+Alt+Shift+J" };
            if (search is not null) keys["Search"] = search;
            File.WriteAllText(file, System.Text.Json.JsonSerializer.Serialize(new { Version = 4, Hotkeys = keys }));
            var result = LayoutSettings.Load(file);
            Assert.Equal(expected, result.Hotkeys["Search"]);
            Assert.Equal("", result.Hotkeys["NewNote"]);
            Assert.Equal("Ctrl+Alt+Shift+J", result.Hotkeys["ToggleCollapsed"]);
            Assert.Equal(6, result.Hotkeys.Count);
            Assert.DoesNotContain("ShowList", result.Hotkeys.Keys);
        });

    [Fact]
    public void BackupPreservesOriginalAndNoticeCanBeAcknowledgedAcrossRestart()
        => WithFile(file =>
        {
            const string original = "{\"Version\":4,\"Hotkeys\":{\"HideAll\":\"Ctrl+Alt+Shift+N\",\"ExpandAll\":\"Ctrl+Alt+Shift+F\"}}";
            File.WriteAllText(file, original);
            var result = LayoutSettings.Load(file);
            Assert.Equal(original, File.ReadAllText(file));
            Assert.Equal(original, File.ReadAllText(file + ".pre-v5.bak"));
            Assert.True(result.NeedsShortcutSimplificationNotice);
            Assert.Equal("", result.Hotkeys["NewNote"]);
            Assert.Equal("", result.Hotkeys["Search"]);
            (result with { NeedsShortcutSimplificationNotice = false }).Save(file);
            var reloaded = LayoutSettings.Load(file);
            Assert.Equal(5, reloaded.Version);
            Assert.False(reloaded.NeedsShortcutSimplificationNotice);
            Assert.Equal(original, File.ReadAllText(file + ".pre-v5.bak"));
            HotkeyService.Validate(reloaded.Hotkeys);
        });

    [Fact]
    public void NewSettingsNeedNoMigrationNoticeOrBackup()
        => WithFile(file =>
        {
            var fresh = LayoutSettings.Load(file);
            Assert.False(fresh.NeedsShortcutSimplificationNotice);
            fresh.Save(file);
            Assert.False(LayoutSettings.Load(file).NeedsShortcutSimplificationNotice);
            Assert.False(File.Exists(file + ".pre-v5.bak"));
        });

    [Fact]
    public void InvalidLegacySettingsDoNotCreateBackupOrOverwriteSource()
        => WithFile(file =>
        {
            const string invalid = "{\"Version\":4,\"OverlayOpacity\":5}";
            File.WriteAllText(file, invalid);
            Assert.Throws<InvalidDataException>(() => LayoutSettings.Load(file));
            Assert.Equal(invalid, File.ReadAllText(file));
            Assert.False(File.Exists(file + ".pre-v5.bak"));
        });

    [Fact]
    public void UnknownV5CommandIsRejectedWithoutChangingSource()
        => WithFile(file =>
        {
            const string source = "{\"Version\":5,\"Hotkeys\":{\"ShowList\":\"Ctrl+Alt+Shift+L\"}}";
            File.WriteAllText(file, source);
            Assert.Throws<InvalidDataException>(() => LayoutSettings.Load(file));
            Assert.Equal(source, File.ReadAllText(file));
            Assert.False(File.Exists(file + ".pre-v5.bak"));
        });

    [Fact]
    public void MalformedRetiredGestureIsReportedBeforeMigration()
        => WithFile(file =>
        {
            const string source = "{\"Version\":4,\"Hotkeys\":{\"HideAll\":\"Ctrl+NotAKey\"}}";
            File.WriteAllText(file, source);
            Assert.Throws<ArgumentException>(() => LayoutSettings.Load(file));
            Assert.Equal(source, File.ReadAllText(file));
            Assert.False(File.Exists(file + ".pre-v5.bak"));
        });

    [Fact]
    public void PartialV5DefaultsRespectCustomCollisionAndExplicitlyDisabledBindings()
        => WithFile(file =>
        {
            new LayoutSettings { Hotkeys = new() { ["NewNote"] = "Ctrl+Alt+Shift+F", ["ToggleOverlay"] = "" } }.Save(file);
            var result = LayoutSettings.Load(file);
            Assert.Equal(6, result.Hotkeys.Count);
            Assert.Equal("Ctrl+Alt+Shift+F", result.Hotkeys["NewNote"]);
            Assert.Equal("", result.Hotkeys["Search"]);
            Assert.Equal("", result.Hotkeys["ToggleOverlay"]);
            Assert.False(result.NeedsShortcutSimplificationNotice);
            HotkeyService.Validate(result.Hotkeys);
        });
    private static void WithFile(Action<string> test)
    {
        string file = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try { test(file); }
        finally { File.Delete(file); File.Delete(file + ".pre-v5.bak"); }
    }
}
