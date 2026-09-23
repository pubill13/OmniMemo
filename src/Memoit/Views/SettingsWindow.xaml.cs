using System;
using System.Windows;

namespace Memoit.Views;

public partial class SettingsWindow : Window
{
    private bool _ready;
    public event Action<bool>? AutoStartChanged;
    public event Action? BackupRequested;
    public event Action? RestoreRequested;

    public SettingsWindow(bool autoStart, string dataDirectory)
    {
        InitializeComponent();
        AutoStartBox.IsChecked = autoStart;
        DataPath.Text = dataDirectory;
        _ready = true;
    }

    public void SetAutoStart(bool value)
    {
        _ready = false;
        AutoStartBox.IsChecked = value;
        _ready = true;
    }

    private void OnAutoStart(object sender, RoutedEventArgs e) { if (_ready) AutoStartChanged?.Invoke(AutoStartBox.IsChecked == true); }
    private void OnBackup(object sender, RoutedEventArgs e) => BackupRequested?.Invoke();
    private void OnRestore(object sender, RoutedEventArgs e) => RestoreRequested?.Invoke();
    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
