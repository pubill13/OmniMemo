using System.Windows;
using System.Windows.Interop;

namespace Memoit.Services;

public static class Dialogs
{
    public static MessageBoxResult Show(string message, string title = "OmniMemo",
        MessageBoxButton buttons = MessageBoxButton.OK, MessageBoxImage image = MessageBoxImage.None)
    {
        var owner = Application.Current?.Windows.Cast<Window>().FirstOrDefault(w => w.IsActive)
            ?? Application.Current?.Windows.Cast<Window>().FirstOrDefault(w => w.IsVisible)
            ?? Application.Current?.MainWindow;
        bool temporary = owner is null;
        owner ??= new Window { ShowInTaskbar = false, WindowStyle = WindowStyle.ToolWindow };
        new WindowInteropHelper(owner).EnsureHandle();
        try { return MessageBox.Show(owner, message, title, buttons, image); }
        finally { if (temporary) owner.Close(); }
    }
}
