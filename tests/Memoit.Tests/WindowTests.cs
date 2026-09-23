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
                    Assert.True(window.MinWidth >= 240);
                    Assert.True(window.MinHeight >= 180);
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
