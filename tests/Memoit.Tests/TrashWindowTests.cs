using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using Memoit.Models;
using Memoit.Views;
using Xunit;

namespace Memoit.Tests;

[Collection("WPF")]
public sealed class TrashWindowTests
{
    [Fact]
    public void ActiveRefreshPreservesQuerySelectionAndExcludesTrash() => Sta(() =>
    {
        var window = new MainWindow { AllowClose = true };
        try
        {
            var active = new Note { Body = "needle" };
            window.SetNotes([active, new Note { Body = "needle deleted", DeletedAt = DateTimeOffset.UtcNow }]);
            var search = (TextBox)window.FindName("SearchBox"); search.Text = "needle";
            var list = (ListBox)window.FindName("NoteList"); list.SelectedIndex = 0;
            int refreshed = 0; window.RefreshRequested += () => { refreshed++; window.SetNotes([active]); };
            ((Button)window.FindName("ActiveButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(1, refreshed); Assert.Equal("needle", search.Text);
            Assert.Equal(active.Id, Assert.Single(window.SelectedIds)); Assert.Single(list.Items.Cast<object>());
            Assert.Equal("새로고침 완료", ((Button)window.FindName("ActiveButton")).Content);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void TrashSearchSelectionAndCloseAreIndependent() => Sta(() =>
    {
        var window = new TrashWindow();
        var first = new Note { Body = "first", DeletedAt = DateTimeOffset.UtcNow };
        var second = new Note { Body = "second", DeletedAt = DateTimeOffset.UtcNow };
        window.SetNotes([first, second, new Note { Body = "active" }]);
        var list = Field<ListBox>(window, "list");
        Assert.Equal(2, list.Items.Count);
        list.SelectedItems.Add(first); list.SelectedItems.Add(second);
        IReadOnlyList<Guid>? requested = null; window.PermanentDeleteSelectedRequested += ids => requested = ids;
        Field<Button>(window, "deleteSelected").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(2, requested!.Count);
        window.SetNotes([second]); Assert.Equal(second.Id, Assert.Single(window.SelectedIds));
        Field<TextBox>(window, "search").Text = "missing"; Assert.Empty(window.SelectedIds);
        bool closed = false; window.Closed += (_, _) => closed = true; window.Close(); Assert.True(closed);
        var reopened = new TrashWindow(); Assert.Empty(reopened.SelectedIds); reopened.Close();
    });
    private static T Field<T>(object value, string name) => (T)value.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(value)!;
    private static void Sta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() => { try { action(); } catch (Exception ex) { error = ex; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (error is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
    }
}
