using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Automation;
using Memoit.Models;

namespace Memoit.Views;

public sealed class TrashWindow : Window
{
    public bool AllowClose { get; set; }
    private readonly ListBox list = new() { SelectionMode = SelectionMode.Extended };
    private readonly TextBox search = new();
    private readonly Button deleteSelected = new() { Content = "선택 영구 삭제", IsEnabled = false, Padding = new Thickness(10, 6, 10, 6) };
    private readonly TextBlock status = new() { Margin = new Thickness(0, 8, 0, 8) };
    private IReadOnlyList<Note> notes = [];
    public event Action<Guid>? RestoreRequested;
    public event Action<Guid>? PermanentDeleteRequested;
    public event Action<IReadOnlyList<Guid>>? PermanentDeleteSelectedRequested;
    public IReadOnlyList<Guid> SelectedIds => list.SelectedItems.Cast<Note>().Select(n => n.Id).ToArray();

    public TrashWindow()
    {
        Title = "OmniMemo · 휴지통"; Width = 560; Height = 620; MinWidth = 430; MinHeight = 360;
        ShowInTaskbar = false; WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = new System.Windows.Media.FontFamily("Malgun Gothic");
        var root = new DockPanel { Margin = new Thickness(20) };
        var heading = new TextBlock { Text = "휴지통", FontSize = 24, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 12) };
        DockPanel.SetDock(heading, Dock.Top); root.Children.Add(heading);
        search.Margin = new Thickness(0, 0, 0, 12); search.Padding = new Thickness(9); search.ToolTip = "휴지통 검색";
        AutomationProperties.SetName(search, "휴지통 검색"); AutomationProperties.SetName(list, "휴지통 목록");
        search.TextChanged += (_, _) => Refresh(); DockPanel.SetDock(search, Dock.Top); root.Children.Add(search);
        deleteSelected.Click += (_, _) => PermanentDeleteSelectedRequested?.Invoke(SelectedIds);
        DockPanel.SetDock(deleteSelected, Dock.Top); root.Children.Add(deleteSelected);
        DockPanel.SetDock(status, Dock.Top); root.Children.Add(status);
        list.SelectionChanged += (_, _) => deleteSelected.IsEnabled = list.SelectedItems.Count > 0;
        list.BorderThickness = new Thickness(0); list.Background = System.Windows.Media.Brushes.Transparent;
        list.ItemTemplate = CreateTemplate(); root.Children.Add(list); Content = root;
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { Close(); e.Handled = true; } };
    }

    public void SetNotes(IEnumerable<Note> values) { notes = values.Where(n => n.DeletedAt is not null).ToArray(); Refresh(); }
    private void Refresh()
    {
        var selected = SelectedIds.ToHashSet();
        var q = search.Text.Trim();
        var rows = notes.Where(n => n.Body.Contains(q, StringComparison.OrdinalIgnoreCase)).OrderByDescending(n => n.DeletedAt).ToArray();
        list.ItemsSource = rows;
        foreach (var note in rows.Where(n => selected.Contains(n.Id))) list.SelectedItems.Add(note);
        status.Text = rows.Length == 0 ? "휴지통이 비어 있거나 검색 결과가 없습니다." : $"휴지통 · {rows.Length}개 · Ctrl/Shift로 여러 개 선택";
    }
    private DataTemplate CreateTemplate()
    {
        var template = new DataTemplate(typeof(Note));
        var border = new FrameworkElementFactory(typeof(Border)); border.SetValue(Border.PaddingProperty, new Thickness(12)); border.SetValue(Border.MarginProperty, new Thickness(0, 0, 0, 8)); border.SetValue(Border.BackgroundProperty, new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(250, 248, 242)));
        var panel = new FrameworkElementFactory(typeof(DockPanel));
        var buttons = new FrameworkElementFactory(typeof(StackPanel)); buttons.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal); buttons.SetValue(DockPanel.DockProperty, Dock.Right);
        var restore = new FrameworkElementFactory(typeof(Button)); restore.SetValue(Button.ContentProperty, "복원"); restore.SetValue(Button.MarginProperty, new Thickness(0, 0, 6, 0)); restore.AddHandler(Button.ClickEvent, new RoutedEventHandler((s, _) => RestoreRequested?.Invoke(((Note)((FrameworkElement)s).DataContext).Id))); buttons.AppendChild(restore);
        var erase = new FrameworkElementFactory(typeof(Button)); erase.SetValue(Button.ContentProperty, "영구 삭제"); erase.AddHandler(Button.ClickEvent, new RoutedEventHandler((s, _) => PermanentDeleteRequested?.Invoke(((Note)((FrameworkElement)s).DataContext).Id))); buttons.AppendChild(erase); panel.AppendChild(buttons);
        var text = new FrameworkElementFactory(typeof(TextBlock)); text.SetValue(TextBlock.FontSizeProperty, 15d); text.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold); text.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis); text.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(Note.Title))); panel.AppendChild(text);
        border.AppendChild(panel); template.VisualTree = border; return template;
    }
}
