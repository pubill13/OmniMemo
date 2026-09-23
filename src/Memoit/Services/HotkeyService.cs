using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;

namespace Memoit.Services;

/// <summary>Owns registrations on the UI thread. New registrations succeed before old bindings are released.</summary>
public sealed class HotkeyService : IDisposable
{
    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hwnd, int id);
    private readonly HwndSource source;
    private Dictionary<string, string> configured = [];
    private Dictionary<string, int> registered = [];
    private Dictionary<int, string> commands = [];
    private int nextId = 1;
    private bool disposed;
    public bool Suspended { get; private set; }
    public event Action<string>? Command;

    public HotkeyService()
    {
        source = new HwndSource(new HwndSourceParameters("OmniMemo hotkeys") { ParentWindow = new IntPtr(-3), WindowStyle = 0 });
        source.AddHook(OnMessage);
    }

    public static string Normalize(string gesture)
    {
        if (string.IsNullOrWhiteSpace(gesture)) return "";
        var pieces = gesture.Split('+', StringSplitOptions.TrimEntries);
        ModifierKeys modifiers = ModifierKeys.None;
        Key key = Key.None;
        foreach (string piece in pieces)
        {
            ModifierKeys modifier = piece.ToUpperInvariant() switch { "CTRL" or "CONTROL" => ModifierKeys.Control, "ALT" => ModifierKeys.Alt,
                "SHIFT" => ModifierKeys.Shift, "WIN" or "WINDOWS" => ModifierKeys.Windows, _ => ModifierKeys.None };
            if (modifier != ModifierKeys.None)
            {
                if ((modifiers & modifier) != 0) throw new ArgumentException("중복된 보조 키입니다: " + gesture);
                modifiers |= modifier;
            }
            else
            {
                string name = piece.Length == 1 && char.IsAsciiDigit(piece[0]) ? "D" + piece : piece;
                if (key != Key.None || !Enum.TryParse(name, true, out key) || !Enum.IsDefined(key) || key is Key.None or Key.System
                    or Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin
                    || KeyInterop.VirtualKeyFromKey(key) == 0)
                    throw new ArgumentException("올바른 단축키가 아닙니다: " + gesture);
            }
        }
        if (key == Key.None) throw new ArgumentException("일반 키가 필요합니다: " + gesture);
        var prefix = ((modifiers & ModifierKeys.Control) != 0 ? "Ctrl+" : "") + ((modifiers & ModifierKeys.Alt) != 0 ? "Alt+" : "")
            + ((modifiers & ModifierKeys.Shift) != 0 ? "Shift+" : "") + ((modifiers & ModifierKeys.Windows) != 0 ? "Win+" : "");
        string keyName = key >= Key.D0 && key <= Key.D9 ? ((int)key - (int)Key.D0).ToString() : key == Key.Return ? "Enter" : key.ToString();
        return prefix + keyName;
    }

    private static (Key Key, ModifierKeys Modifiers) Parse(string normalized)
    {
        string[] parts = normalized.Split('+');
        string name = parts[^1];
        var key = Enum.Parse<Key>(name.Length == 1 && char.IsAsciiDigit(name[0]) ? "D" + name : name, true);
        var modifiers = ModifierKeys.None;
        if (parts.Contains("Ctrl")) modifiers |= ModifierKeys.Control;
        if (parts.Contains("Alt")) modifiers |= ModifierKeys.Alt;
        if (parts.Contains("Shift")) modifiers |= ModifierKeys.Shift;
        if (parts.Contains("Win")) modifiers |= ModifierKeys.Windows;
        return (key, modifiers);
    }

    public static void Validate(IReadOnlyDictionary<string, string> values)
    {
        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var pair in values)
        {
            if (!HotkeyDefaults.Labels.ContainsKey(pair.Key)) throw new ArgumentException("알 수 없는 단축키 동작: " + pair.Key);
            string gesture = Normalize(pair.Value);
            if (gesture == "") continue;
            if (!used.Add(gesture)) throw new ArgumentException("단축키가 중복됩니다: " + gesture);
            var (key, modifiers) = Parse(gesture);
            if ((modifiers & ModifierKeys.Windows) != 0 || key == Key.F12 || modifiers == ModifierKeys.None
                || gesture is "Ctrl+N" or "Ctrl+F" or "Ctrl+Escape" or "Ctrl+Shift+Escape" or "Alt+Tab" or "Alt+Shift+Tab"
                    or "Alt+F4" or "Alt+Escape" or "Alt+Shift+Escape" or "Alt+Space" or "Ctrl+Alt+Delete"
                    or "Ctrl+Alt+Tab" or "Ctrl+Alt+Shift+Tab" or "Ctrl+Alt+Escape")
                throw new ArgumentException("앱 또는 Windows에서 사용하는 단축키입니다: " + gesture);
        }
    }

    public static bool Matches(string gesture, Key key, ModifierKeys modifiers)
    {
        var normalized = Normalize(gesture);
        if (normalized == "") return false;
        var parsed = Parse(normalized);
        return parsed.Key == key && parsed.Modifiers == modifiers;
    }

    public void Configure(IReadOnlyDictionary<string, string> values)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        source.Dispatcher.VerifyAccess();
        Validate(values);
        var desired = values.ToDictionary(p => p.Key, p => Normalize(p.Value), StringComparer.Ordinal);
        if (!Suspended) Install(desired);
        configured = desired;
    }

    private void Install(Dictionary<string, string> desired)
    {
        var next = new Dictionary<string, int>();
        var nextCommands = new Dictionary<int, string>();
        var added = new List<int>();
        try
        {
            foreach (var pair in desired.Where(p => p.Key != "ToggleCurrent" && p.Value != ""))
            {
                if (!registered.TryGetValue(pair.Value, out int id))
                {
                    id = nextId++;
                    var (key, modifiers) = Parse(pair.Value);
                    if (!RegisterHotKey(source.Handle, id, (uint)modifiers | 0x4000, (uint)KeyInterop.VirtualKeyFromKey(key)))
                        throw new InvalidOperationException("다른 프로그램이 단축키를 사용 중이거나 등록할 수 없습니다: " + pair.Value);
                    added.Add(id);
                }
                next.Add(pair.Value, id);
                nextCommands.Add(id, pair.Key);
            }
        }
        catch { foreach (int id in added) UnregisterHotKey(source.Handle, id); throw; }
        foreach (var old in registered.Where(p => !next.ContainsKey(p.Key))) UnregisterHotKey(source.Handle, old.Value);
        registered = next;
        commands = nextCommands;
    }

    public void BeginCapture()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        source.Dispatcher.VerifyAccess();
        if (Suspended) return;
        Suspended = true;
        foreach (int id in registered.Values) UnregisterHotKey(source.Handle, id);
        registered.Clear(); commands.Clear();
    }

    public void EndCapture()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        source.Dispatcher.VerifyAccess();
        if (!Suspended) return;
        Install(configured);
        Suspended = false;
    }

    private IntPtr OnMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == 0x0312 && !Suspended && commands.TryGetValue(wParam.ToInt32(), out string? command))
        { handled = true; Command?.Invoke(command); }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (disposed) return;
        source.Dispatcher.VerifyAccess();
        foreach (int id in registered.Values) UnregisterHotKey(source.Handle, id);
        source.RemoveHook(OnMessage); source.Dispose(); disposed = true;
    }
}
