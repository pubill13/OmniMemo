using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using Memoit.Models;
using Memoit.ViewModels;
using Memoit.Views;
using System.Reflection;
using System.Windows.Controls;
using Xunit;

namespace Memoit.Tests;

/// <summary>Meaningful smoke and interaction coverage for the WPF views.</summary>
[Collection("WPF")]
public sealed class WindowTests
{
    [Fact]
    public void PinClickUpdatesOnceAndUsesIconFillWithoutCheckedBackground() => RunSta(() =>
    {
        using var vm = new NoteViewModel(new Note(), _ => Task.CompletedTask);
        var window = new NoteWindow(vm);
        try
        {
            window.Show(); window.UpdateLayout();
            var pin = (System.Windows.Controls.Primitives.ToggleButton)window.FindName("PinToggle");
            Assert.True(System.Windows.Shell.WindowChrome.GetIsHitTestVisibleInChrome(pin));
            var toggle = (System.Windows.Automation.Provider.IToggleProvider)new System.Windows.Automation.Peers.ToggleButtonAutomationPeer(pin);
            toggle.Toggle();
            Assert.True(vm.IsPinned); Assert.True(window.Topmost);
            var head = (System.Windows.Shapes.Path)pin.Template.FindName("PinHead", pin);
            Assert.Equal(System.Windows.SystemParameters.HighContrast ? System.Windows.SystemColors.WindowTextColor : System.Windows.Media.Color.FromRgb(0x17, 0x6b, 0x61), ((System.Windows.Media.SolidColorBrush)head.Fill).Color);
            toggle.Toggle();
            Assert.False(vm.IsPinned); Assert.False(window.Topmost);
            Assert.Equal(System.Windows.Media.Colors.Transparent, ((System.Windows.Media.SolidColorBrush)head.Fill).Color);
        }
        finally { window.AllowClose = true; window.Close(); }
    });

    [Fact]
    public void ListSelectionSurvivesSavedNoteRefreshAndTracksVisibleSearchResults() => RunSta(() =>
    {
        var window = new MainWindow();
        var first = new Note { Body = "alpha" };
        var second = new Note { Body = "beta" };
        try
        {
            window.SetNotes([first, second]);
            var box = (ListBox)window.FindName("NoteList");
            box.SelectedItems.Add(box.Items[0]); box.SelectedItems.Add(box.Items[1]);
            Assert.Equal(2, window.SelectedIds.Count);
            window.SetNotes([first with { Body = "alpha edited" }, second]);
            Assert.Equal(2, window.SelectedIds.Count);
            Assert.Equal(2, box.SelectedItems.Count);
            ((TextBox)window.FindName("SearchBox")).Text = "alpha";
            Assert.Equal(first.Id, Assert.Single(window.SelectedIds));
            window.SetNotes([first, second], true);
            Assert.Equal(first.Id, Assert.Single(window.SelectedIds));
            Assert.False(window.ShowingTrash);
        }
        finally { window.AllowClose = true; window.Close(); }
    });

    [Fact]
    public void NoteWindow_BindsMultilineKoreanAndEmojiWithoutReplacingEditor()
    {
        RunSta(() =>
        {
            var note = new Note { Body = "첫 줄\n두 번째 😊" };
            using var vm = new NoteViewModel(note, _ => Task.CompletedTask);
            var window = new NoteWindow(vm);
            window.Show();
            window.Editor.Focus();
            window.Editor.CaretIndex = window.Editor.Text.Length;
            window.Editor.AppendText("\n추가");
            window.UpdateLayout();
            window.Editor.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
            Assert.Equal("첫 줄\n두 번째 😊\n추가", vm.Body);
            var caret = window.Editor.CaretIndex;
            window.Editor.Undo();
            Assert.Equal("첫 줄\n두 번째 😊", window.Editor.Text);
            Assert.True(caret >= window.Editor.CaretIndex);
            window.AllowClose = true;
            window.Close();
        });
    }

    [Fact]
    public void MainWindow_FiltersThousandNotesAndReportsTiming()
    {
        RunSta(() =>
        {
            var notes = Enumerable.Range(0, 1000).Select(i => new Note
            {
                Body = i % 100 == 0 ? $"업무 검색어 {i}" : $"일반 메모 {i}",
                UpdatedAt = DateTimeOffset.UtcNow.AddMinutes(-i)
            }).ToList();
            var window = new MainWindow();
            window.SetNotes(notes);
            var timer = Stopwatch.StartNew();
            window.FocusSearch();
            var search = (TextBox)typeof(MainWindow).GetField("SearchBox", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
            search.Text = "검색어";
            timer.Stop();
            Console.WriteLine($"MainWindow 1,000개 검색: {timer.ElapsedMilliseconds}ms");
            var list = (ListBox)typeof(MainWindow).GetField("NoteList", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
            Assert.Equal(10, list.Items.Count);
            window.AllowClose = true;
            window.Close();
        });
    }

    [Fact]
    public void NoteWindows_ConstructTwentyAndRespectMinimumBounds()
    {
        RunSta(() =>
        {
            var windows = new List<(NoteWindow Window, NoteViewModel Vm)>();
            try
            {
                for (var i = 0; i < 20; i++)
                {
                    var vm = new NoteViewModel(new Note(), _ => Task.CompletedTask);
                    var window = new NoteWindow(vm);
                    windows.Add((window, vm));
                    Assert.True(window.MinWidth >= 200);
                    Assert.True(window.MinHeight >= 150);
                    window.Editor.Text = $"메모 {i}";
                }
            }
            finally
            {
                foreach (var (window, vm) in windows) { window.AllowClose = true; window.Close(); vm.Dispose(); }
            }
        });
    }

    [Fact]
    public void NoteWindow_CanCollapseToAndRestoreCompactTile()
    {
        RunSta(() =>
        {
            using var vm = new NoteViewModel(new Note(), _ => Task.CompletedTask);
            var window = new NoteWindow(vm);
            var width = window.Width;
            window.ToggleCollapsed();
            Assert.True(window.IsCollapsed);
            Assert.Equal(36, window.Width);
            window.ToggleCollapsed();
            Assert.False(window.IsCollapsed);
            Assert.Equal(width, window.Width);
            window.AllowClose = true;
            window.Close();
        });
    }

    [Fact]
    public void DirectStateChange_NotifiesAfterFinalBounds_AndBatchDoesNotNotify()
    {
        RunSta(() =>
        {
            using var vm = new NoteViewModel(new Note(), _ => Task.CompletedTask);
            var window = new NoteWindow(vm) { ShowActivated = false };
            try
            {
                window.Show(); window.UpdateLayout();
                int notifications = 0;
                window.CollapsedStateChanged += () =>
                {
                    notifications++;
                    Assert.Equal(window.Left, vm.Snapshot.Left);
                    Assert.Equal(window.Top, vm.Snapshot.Top);
                    Assert.Equal(window.IsCollapsed ? 36 : vm.Snapshot.Width, window.Width);
                };
                window.ToggleCollapsed(false);
                window.ToggleCollapsed(false);
                Assert.Equal(2, notifications);
                window.PrepareState(false, new System.Windows.Rect(window.Left, window.Top, window.Width, window.Height));
                window.EndArrangement();
                Assert.Equal(2, notifications);
            }
            finally { window.AllowClose = true; window.Close(); }
        });
    }

    private static void RunSta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { error = ex; }
            finally { }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null) throw new Xunit.Sdk.XunitException(error.ToString());
    }
}
