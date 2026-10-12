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
            Assert.Equal("전체 메모", ((Button)window.FindName("ActiveButton")).Content);
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
    [Fact]
    public void EmptyTrashIncludesFilteredOutNotesButNeverActiveNotes() => Sta(() =>
    {
        var window = new TrashWindow();
        try
        {
            var first = new Note { Body = "find me", DeletedAt = DateTimeOffset.UtcNow };
            var second = new Note { Body = "other", DeletedAt = DateTimeOffset.UtcNow };
            window.SetNotes([first, second, new Note { Body = "active" }]);
            Field<TextBox>(window, "search").Text = "find me";
            Assert.Single(Field<ListBox>(window, "list").Items.Cast<object>());
            IReadOnlyList<Guid>? requested = null;
            window.EmptyTrashRequested += ids => requested = ids;
            Field<Button>(window, "emptyTrash").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(new[] { first.Id, second.Id }.Order(), requested!.Order());
            window.SetNotes([]);
            Assert.False(Field<Button>(window, "emptyTrash").IsEnabled);
        }
        finally { window.Close(); }
    });
    private static T Field<T>(object value, string name) => (T)value.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)!.GetValue(value)!;
    [Theory]
    [InlineData(480)]
    [InlineData(640)]
    public void TrashLayoutKeepsActionsInsideCards(int width) => Sta(() =>
    {
        var window = new TrashWindow();
        try
        {
            window.SetNotes(Enumerable.Range(0, 4).Select(i => new Note
            {
                Body = i == 0 ? new string('긴', 100) : $"삭제한 메모 {i + 1}",
                DeletedAt = DateTimeOffset.Now.AddDays(-i)
            }));
            var root = (FrameworkElement)window.Content;
            root.Measure(new Size(width, 600)); root.Arrange(new Rect(0, 0, width, 600)); root.UpdateLayout();
            foreach (var button in Descendants<Button>(root))
            {
                var bounds = button.TransformToAncestor(root).TransformBounds(new Rect(button.RenderSize));
                Assert.True(bounds.Left >= 0 && bounds.Right <= width && bounds.Bottom <= 600);
                Assert.True(button.ActualHeight >= 34);
            }
            string directory = System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory, "../../../../../artifacts/verification1112"));
            System.IO.Directory.CreateDirectory(directory);
            var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(width, 600, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            bitmap.Render(root);
            var png = new System.Windows.Media.Imaging.PngBitmapEncoder();
            png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
            using var file = System.IO.File.Create(System.IO.Path.Combine(directory, $"trash-{width}.png")); png.Save(file);
        }
        finally { window.Close(); }
    });
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }
    private static void Sta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() => { try { action(); } catch (Exception ex) { error = ex; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (error is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
    }
}
