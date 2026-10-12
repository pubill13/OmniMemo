using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Memoit.Models;

namespace Memoit.Views;

public sealed partial class TrashWindow : Window
{
    public bool AllowClose { get; set; }
    private IReadOnlyList<Note> notes = [];
    public event Action<Guid>? RestoreRequested;
    public event Action<Guid>? PermanentDeleteRequested;
    public event Action<IReadOnlyList<Guid>>? PermanentDeleteSelectedRequested;
    public event Action<IReadOnlyList<Guid>>? EmptyTrashRequested;
    public IReadOnlyList<Guid> SelectedIds => list.SelectedItems.Cast<Note>().Select(n => n.Id).ToArray();

    public TrashWindow()
    {
        InitializeComponent();
        search.TextChanged += (_, _) => Refresh();
        deleteSelected.Click += (_, _) => PermanentDeleteSelectedRequested?.Invoke(SelectedIds);
        emptyTrash.Click += (_, _) => EmptyTrashRequested?.Invoke(notes.Select(n => n.Id).ToArray());
        list.SelectionChanged += (_, _) => UpdateSelection();
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { Close(); e.Handled = true; } };
        Refresh();
    }

    public void SetNotes(IEnumerable<Note> values)
    {
        notes = values.Where(n => n.DeletedAt is not null).ToArray();
        Refresh();
    }
    private void Refresh()
    {
        var selected = SelectedIds.ToHashSet();
        var query = search.Text.Trim();
        var rows = notes.Where(n => n.Body.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(n => n.DeletedAt).ToArray();
        list.ItemsSource = rows;
        foreach (var note in rows.Where(n => selected.Contains(n.Id))) list.SelectedItems.Add(note);
        emptyTrash.IsEnabled = notes.Count > 0;
        empty.Visibility = rows.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        empty.Text = notes.Count == 0 ? "휴지통이 비어 있습니다." : "검색 결과가 없습니다.";
        UpdateSelection();
    }
    private void UpdateSelection()
    {
        deleteSelected.IsEnabled = list.SelectedItems.Count > 0;
        status.Text = search.Text.Trim().Length == 0 ? $"전체 {notes.Count}개" : $"검색 {list.Items.Count}개 / 전체 {notes.Count}개";
        if (list.SelectedItems.Count > 0) status.Text += $" · {list.SelectedItems.Count}개 선택";
    }
    private void OnRestore(object sender, RoutedEventArgs e) => RestoreRequested?.Invoke(((Note)((FrameworkElement)sender).DataContext).Id);
    private void OnDelete(object sender, RoutedEventArgs e) => PermanentDeleteRequested?.Invoke(((Note)((FrameworkElement)sender).DataContext).Id);
}
