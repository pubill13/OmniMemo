using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Memoit.Models;

namespace Memoit.Views;

public partial class MainWindow : Window
{
    private List<Note> _notes = [];
    public bool AllowClose { get; set; }
    public string SearchText => SearchBox.Text;
    public bool ShowingTrash { get; private set; }
    public event Action? NewNoteRequested;
    public event Action? SettingsRequested;
    public event Action<Guid>? OpenNoteRequested;
    public event Action<Guid>? RestoreNoteRequested;
    public event Action<Guid>? PermanentDeleteRequested;
    public event Action? SearchChanged;

    public MainWindow()
    {
        InitializeComponent();
        Closing += (_, e) => { if (!AllowClose) { e.Cancel = true; Hide(); } };
    }

    public void SetNotes(IEnumerable<Note> notes, bool trash = false)
    {
        _notes = notes.ToList();
        ShowingTrash = trash;
        RefreshList();
    }

    public void FocusSearch() { SearchBox.Focus(); SearchBox.SelectAll(); }

    private void RefreshList()
    {
        if (NoteList is null) return;
        var query = SearchText.Trim();
        var items = _notes.Where(n => (n.DeletedAt != null) == ShowingTrash && n.Body.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(n => n.UpdatedAt).Select(n => new NoteRow(n, ShowingTrash)).ToList();
        NoteList.ItemsSource = items;
        SectionLabel.Text = $"{(ShowingTrash ? "휴지통" : "전체 메모")} · {items.Count}개";
        EmptyLabel.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyLabel.Text = query.Length > 0 ? "검색 결과가 없습니다." : ShowingTrash ? "휴지통이 비어 있습니다." : "메모가 없습니다. 새 메모로 시작하세요.";
        ActiveButton.FontWeight = ShowingTrash ? FontWeights.Normal : FontWeights.Bold;
        TrashButton.FontWeight = ShowingTrash ? FontWeights.Bold : FontWeights.Normal;
    }

    private void OnSearchTextChanged(object sender, TextChangedEventArgs e) { RefreshList(); SearchChanged?.Invoke(); }
    private void OnActive(object sender, RoutedEventArgs e) { ShowingTrash = false; RefreshList(); SearchChanged?.Invoke(); }
    private void OnTrash(object sender, RoutedEventArgs e) { ShowingTrash = true; RefreshList(); SearchChanged?.Invoke(); }
    private void OnNew(object sender, RoutedEventArgs e) => NewNoteRequested?.Invoke();
    private void OnSettings(object sender, RoutedEventArgs e) => SettingsRequested?.Invoke();
    private void OnOpen(object sender, MouseButtonEventArgs e) { if (!ShowingTrash && NoteList.SelectedItem is NoteRow row) OpenNoteRequested?.Invoke(row.Id); }
    private void OnOpenButton(object sender, RoutedEventArgs e) => OpenNoteRequested?.Invoke((Guid)((Button)sender).Tag);
    private void OnRestore(object sender, RoutedEventArgs e) => RestoreNoteRequested?.Invoke((Guid)((Button)sender).Tag);
    private void OnPermanentDelete(object sender, RoutedEventArgs e) => PermanentDeleteRequested?.Invoke((Guid)((Button)sender).Tag);
    private void OnListKeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter && !ShowingTrash && NoteList.SelectedItem is NoteRow row) { OpenNoteRequested?.Invoke(row.Id); e.Handled = true; } }
    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Control) return;
        if (e.Key == Key.N) { NewNoteRequested?.Invoke(); e.Handled = true; }
        if (e.Key == Key.F) { FocusSearch(); e.Handled = true; }
    }

    private sealed class NoteRow(Note note, bool trash)
    {
        public Guid Id => note.Id;
        public string Title => note.Title;
        public string Preview => note.Body.Length > 180 ? note.Body[..180] + "…" : note.Body;
        public string Color => note.Color;
        public string Modified => note.UpdatedAt.ToLocalTime().ToString("yyyy.MM.dd HH:mm");
        public Visibility TrashVisibility => trash ? Visibility.Visible : Visibility.Collapsed;
        public Visibility ActiveVisibility => trash ? Visibility.Collapsed : Visibility.Visible;
    }
}
