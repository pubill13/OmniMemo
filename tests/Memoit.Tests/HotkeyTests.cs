using System.Windows.Input;
using Memoit.Services;
using Xunit;

namespace Memoit.Tests;

[Collection("WPF")]
public sealed class HotkeyTests
{
    [Theory]
    [InlineData(" shift + control + alt + h ", "Ctrl+Alt+Shift+H")]
    [InlineData("Alt+Ctrl+D1", "Ctrl+Alt+1")]
    [InlineData("ctrl+shift+space", "Ctrl+Shift+Space")]
    [InlineData("", "")]
    public void NormalizationUsesCanonicalModifierAndKeyNames(string value, string expected)
        => Assert.Equal(expected, HotkeyService.Normalize(value));

    [Fact]
    public void DefaultsAreValidAndLocalMatchRequiresExactModifiers()
    {
        var defaults = HotkeyDefaults.Create();
        HotkeyService.Validate(defaults);
        Assert.Equal(16, defaults.Count);
        Assert.True(HotkeyService.Matches(defaults["ToggleCurrent"], Key.Space, ModifierKeys.Control | ModifierKeys.Shift));
        Assert.False(HotkeyService.Matches(defaults["ToggleCurrent"], Key.Space, ModifierKeys.Control));
        Assert.False(HotkeyService.Matches("", Key.Space, ModifierKeys.Control));
    }

    [Theory]
    [InlineData("Ctrl+N")]
    [InlineData("Ctrl+F")]
    [InlineData("Enter")]
    [InlineData("Space")]
    [InlineData("Win+R")]
    [InlineData("Alt+Tab")]
    [InlineData("Ctrl+Alt+Delete")]
    [InlineData("Ctrl+F12")]
    [InlineData("Ctrl+Ctrl+A")]
    [InlineData("Ctrl+A+B")]
    [InlineData("Ctrl+")]
    public void RejectsReservedOrMalformedGestures(string gesture)
        => Assert.Throws<ArgumentException>(() => HotkeyService.Validate(new Dictionary<string, string> { ["Arrange"] = gesture }));

    [Fact]
    public void DuplicateLocalAndGlobalGesturesAreRejectedAfterNormalization()
        => Assert.Throws<ArgumentException>(() => HotkeyService.Validate(new Dictionary<string, string>
        { ["Arrange"] = "Ctrl+Shift+Space", ["ToggleCurrent"] = "shift+control+space" }));

    [Fact]
    public void NativeConflictRollsBackNewRegistrationsAndKeepsPreviousBinding() => OnSta(() =>
    {
        using var owner = new HotkeyService();
        using var competitor = new HotkeyService();
        owner.Configure(Binding("Ctrl+Alt+Shift+F20"));
        competitor.Configure(Binding("Ctrl+Alt+Shift+F21"));
        Assert.Throws<InvalidOperationException>(() => owner.Configure(new Dictionary<string, string>
        { ["Arrange"] = "Ctrl+Alt+Shift+F22", ["SortColor"] = "Ctrl+Alt+Shift+F21" }));
        using var probe = new HotkeyService();
        Assert.Throws<InvalidOperationException>(() => probe.Configure(Binding("Ctrl+Alt+Shift+F20")));
        probe.Configure(Binding("Ctrl+Alt+Shift+F22"));
        owner.Dispose();
        probe.Configure(Binding("Ctrl+Alt+Shift+F20"));
    });

    [Fact]
    public void SwappingCommandsReusesRegisteredGesturesAndCaptureReleasesThem() => OnSta(() =>
    {
        using var owner = new HotkeyService();
        owner.Configure(new Dictionary<string, string> { ["Arrange"] = "Ctrl+Alt+Shift+F20", ["SortColor"] = "Ctrl+Alt+Shift+F21" });
        owner.Configure(new Dictionary<string, string> { ["Arrange"] = "Ctrl+Alt+Shift+F21", ["SortColor"] = "Ctrl+Alt+Shift+F20" });
        owner.BeginCapture();
        Assert.True(owner.Suspended);
        using var competitor = new HotkeyService();
        competitor.Configure(Binding("Ctrl+Alt+Shift+F20"));
        Assert.Throws<InvalidOperationException>(() => owner.EndCapture());
        Assert.True(owner.Suspended);
        // The F21 registration acquired before the F20 conflict must also have been released.
        using var probe = new HotkeyService();
        probe.Configure(Binding("Ctrl+Alt+Shift+F21"));
        probe.Dispose(); competitor.Dispose();
        owner.EndCapture();
        Assert.False(owner.Suspended);
        using var blocked = new HotkeyService();
        Assert.Throws<InvalidOperationException>(() => blocked.Configure(Binding("Ctrl+Alt+Shift+F20")));
    });

    [Fact]
    public void CurrentNoteGestureRemainsLocalAndCaptureCanReplaceBindings() => OnSta(() =>
    {
        using var owner = new HotkeyService();
        owner.Configure(new Dictionary<string, string> { ["ToggleCurrent"] = "Ctrl+Shift+Space" });
        using var other = new HotkeyService();
        other.Configure(Binding("Ctrl+Shift+Space"));
        owner.BeginCapture();
        owner.Configure(Binding("Ctrl+Alt+Shift+F20"));
        owner.EndCapture();
        Assert.Throws<InvalidOperationException>(() => other.Configure(Binding("Ctrl+Alt+Shift+F20")));
    });

    private static Dictionary<string, string> Binding(string gesture) => new() { ["Arrange"] = gesture };
    private static void OnSta(Action test)
    {
        Exception? error = null;
        var thread = new Thread(() => { try { test(); } catch (Exception ex) { error = ex; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)));
        if (error is not null) throw new Xunit.Sdk.XunitException(error.ToString());
    }
}
