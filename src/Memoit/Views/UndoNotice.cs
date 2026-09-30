using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace Memoit.Views;

public sealed class UndoNotice : Border, IDisposable
{
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private readonly Stopwatch elapsed = new();
    private TimeSpan remaining = TimeSpan.FromSeconds(10);
    private readonly TextBlock message = new() { TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
    private readonly Button undo = new() { Content = "실행 취소", Padding = new Thickness(8, 5, 8, 5), Margin = new Thickness(6, 0, 0, 0) };
    private bool working;
    public event Action? UndoRequested;
    public event Action? Expired;
    public UndoNotice(int count)
    {
        Background = new SolidColorBrush(Color.FromRgb(232, 239, 248)); Padding = new Thickness(8);
        CornerRadius = new CornerRadius(6); Margin = new Thickness(0, 8, 0, 0);
        var row = new DockPanel(); DockPanel.SetDock(undo, Dock.Right); row.Children.Add(undo); row.Children.Add(message); Child = row;
        message.Text = $"메모 {count}개를 휴지통으로 이동";
        System.Windows.Automation.AutomationProperties.SetName(undo, "삭제 실행 취소");
        undo.Click += (_, _) => UndoRequested?.Invoke();
        timer.Tick += (_, _) =>
        {
            var delta = elapsed.Elapsed; elapsed.Restart();
            Advance(delta, IsMouseOver || IsKeyboardFocusWithin || working);
        };
        elapsed.Start(); timer.Start();
    }
    public void Advance(TimeSpan elapsedTime, bool paused)
    {
        if (paused || remaining <= TimeSpan.Zero) return;
        remaining -= elapsedTime;
        if (remaining <= TimeSpan.Zero) { Dispose(); Expired?.Invoke(); }
    }
    public void SetWorking(bool value) { working = value; undo.IsEnabled = !value; }
    public void ShowFailure(int count)
    {
        working = false; undo.IsEnabled = true;
        message.Text = $"{count}개 복원 실패 · 다시 시도하세요";
        // Keep the action available after a failed restore; closing/replacing it clears the record.
        timer.Stop();
    }
    public void Dispose() { timer.Stop(); elapsed.Stop(); }
}
