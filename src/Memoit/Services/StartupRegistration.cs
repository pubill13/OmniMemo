using Microsoft.Win32;

namespace Memoit.Services;

public static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "OmniMemo";
    private const string LegacyValueName = "MemoitPersonal";
    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return string.Equals(key?.GetValue(ValueName) as string, Command, StringComparison.OrdinalIgnoreCase);
    }
    private static string Command => $"\"{Environment.ProcessPath ?? throw new InvalidOperationException("실행 파일 경로를 확인할 수 없습니다.")}\"";
    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, true);
        if (enabled) key.SetValue(ValueName, Command);
        else key.DeleteValue(ValueName, false);
        key.DeleteValue(LegacyValueName, false);
    }

    public static void MigrateLegacyRegistration()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, true);
        if (key?.GetValue(LegacyValueName) is not string) return;
        key.SetValue(ValueName, Command);
        key.DeleteValue(LegacyValueName, false);
    }
}
