using System.Windows;
using System.Windows.Controls;
using Memoit.Models;

namespace Memoit.Views;

public sealed class TrashWindow : Window
{
    public bool AllowClose { get; set; }
    private readonly ListBox list = new();
    private readonly TextBox search = new();
    private IReadOnlyList<Note> notes = [];
    public event Action<Guid>? RestoreRequested;
    public event Action<Guid>? PermanentDeleteRequested;

    public TrashWindow()
    {
        Title = "OmniMemo · 휴지통"; Width = 520; Height = 620; MinWidth = 380; MinHeight = 360;
        ShowInTaskbar = false; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new DockPanel { Margin = new Thickness(20) };
        var heading = new TextBlock { Text = "휴지통", FontSize = 24, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 12) };
        DockPanel.SetDock(heading, Dock.Top); root.Children.Add(heading);
        search.Margin = new Thickness(0, 0, 0, 12); search.Padding = new Thickness(9); search.ToolTip = "휴지통 검색";
        search.TextChanged += (_, _) => Refresh(); DockPanel.SetDock(search, Dock.Top); root.Children.Add(search);
        list.BorderThickness = new Thickness(0); list.Background = System.Windows.Media.Brushes.Transparent;
        list.ItemTemplate = CreateTemplate(); root.Children.Add(list); Content = root;
        Activated += (_, _) => Refresh();
        Closing += (_, e) => { if (!AllowClose) { e.Cancel = true; Hide(); } };
    }

    public void SetNotes(IEnumerable<Note> values) { notes = values.Where(n => n.DeletedAt is not null).ToArray(); Refresh(); }
    private void Refresh()
    {
        if (list is null) return;
        var q = search.Text.Trim();
        list.ItemsSource = notes.Where(n => n.Body.Contains(q, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(n => n.UpdatedAt).ToArray();
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
