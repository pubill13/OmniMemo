using System;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Shell;
using Memoit.ViewModels;
using Memoit.Services;

namespace Memoit.Views;

public partial class NoteWindow : Window
{
    private readonly NoteViewModel vm;
    private bool applyingLayout;
    private Point? tilePress;
    private bool draggingTile;
    private Point tileOrigin;
    public bool AllowClose { get; set; }
    public bool IsCollapsed => vm.IsCollapsed;
    public event Action? NewNoteRequested;
    public event Action? SearchRequested;
    public event Action? DeleteRequested;
    public event EventHandler? HideRequested;
    public event Action<Point>? TileMoveRequested;

    public NoteWindow(NoteViewModel vm)
    {
        this.vm = vm;
        InitializeComponent();
        DataContext = vm;
        Left = vm.Snapshot.Left; Top = vm.Snapshot.Top;
        ApplyLayout(); UpdateTileTitle();
        vm.PropertyChanged += OnViewModelChanged;
        Closing += OnClosing;
        Closed += (_, _) => vm.PropertyChanged -= OnViewModelChanged;
        LocationChanged += (_, _) => SaveBounds();
        SizeChanged += (_, _) => SaveBounds();
        Loaded += (_, _) => { if (!IsCollapsed) Editor.Focus(); };
    }
    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(NoteViewModel.IsCollapsed)) ApplyLayout();
        if (e.PropertyName == nameof(NoteViewModel.Title)) UpdateTileTitle();
    }
    private void UpdateTileTitle()
    {
        var enumerator = StringInfo.GetTextElementEnumerator(vm.Title);
        string abbreviation = "";
        for (int i = 0; i < 2 && enumerator.MoveNext(); i++) abbreviation += enumerator.GetTextElement();
        TileTitle.Text = abbreviation;
    }
    private void ApplyLayout()
    {
        applyingLayout = true;
        try
        {
            MinWidth = IsCollapsed ? 36 : 280; MinHeight = IsCollapsed ? 36 : 220;
            Width = IsCollapsed ? 36 : Math.Max(280, vm.Snapshot.Width);
            Height = IsCollapsed ? 36 : Math.Max(220, vm.Snapshot.Height);
            ResizeMode = IsCollapsed ? ResizeMode.NoResize : ResizeMode.CanResize;
            WindowChrome.GetWindowChrome(this).ResizeBorderThickness = new Thickness(IsCollapsed ? 0 : 5);
            Root.Visibility = IsCollapsed ? Visibility.Collapsed : Visibility.Visible;
            CollapsedTile.Visibility = IsCollapsed ? Visibility.Visible : Visibility.Collapsed;
        }
        finally { applyingLayout = false; }
    }
    private void SaveBounds()
    {
        if (!IsLoaded || applyingLayout || WindowState != WindowState.Normal) return;
        if (IsCollapsed) vm.UpdatePosition(Left, Top);
        else vm.UpdateBounds(Left, Top, Width, Height);
    }
    public void ToggleCollapsed()
    {
        SaveBounds();
        vm.IsCollapsed = !vm.IsCollapsed;
        if (IsLoaded) { WindowPlacement.KeepOnScreen(this); SaveBounds(); }
        if (!IsCollapsed) Editor.Focus();
    }
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (AllowClose) return;
        e.Cancel = true; HideRequested?.Invoke(this, EventArgs.Empty);
    }
    private void OnDrag(object sender, MouseButtonEventArgs e)
    {
        if ((e.OriginalSource is Grid || e.OriginalSource is StackPanel || e.OriginalSource is TextBlock) && e.LeftButton == MouseButtonState.Pressed) DragNote();
    }
    private void DragNote()
    {
        DragMove();
        SaveBounds();

    }
    private void OnTileDown(object sender, MouseButtonEventArgs e)
    {
        CollapsedTile.Focus();
        CollapsedTile.CaptureMouse();
        tilePress = PointToScreen(e.GetPosition(this));
        tileOrigin = WindowPlacement.GetPosition(this);
        draggingTile = false;
        e.Handled = true;
    }
    private void OnTileUp(object sender, MouseButtonEventArgs e)
    {
        bool clicked = tilePress.HasValue && !draggingTile;
        tilePress = null;
        CollapsedTile.ReleaseMouseCapture();
        draggingTile = false;
        if (clicked) ToggleCollapsed();
        e.Handled = true;
    }
    private void OnTileMove(object sender, MouseEventArgs e)
    {
        if (tilePress is not Point start || e.LeftButton != MouseButtonState.Pressed) return;
        var point = PointToScreen(e.GetPosition(this));
        var delta = point - start;
        var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(this);
        if (!draggingTile && Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance * dpi.DpiScaleX && Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance * dpi.DpiScaleY) return;
        draggingTile = true;
        TileMoveRequested?.Invoke(tileOrigin + delta);
        e.Handled = true;
    }
    private void OnTileClick(object sender, RoutedEventArgs e)
    {
        tilePress = null;
        if (!draggingTile) ToggleCollapsed();
        e.Handled = true;
    }
    private void OnTileLostCapture(object sender, MouseEventArgs e) { tilePress = null; draggingTile = false; }
    private void OnNew(object sender, RoutedEventArgs e) => NewNoteRequested?.Invoke();
    private void OnSearch(object sender, RoutedEventArgs e) => SearchRequested?.Invoke();
    private void OnDelete(object sender, RoutedEventArgs e) => DeleteRequested?.Invoke();
    private void OnHide(object sender, RoutedEventArgs e) => Close();
    private void OnCollapse(object sender, RoutedEventArgs e) => ToggleCollapsed();
    private void OnMenu(object sender, RoutedEventArgs e)
    {
        var button = (Button)sender; button.ContextMenu!.PlacementTarget = button; button.ContextMenu.IsOpen = true;
    }
    private void OnColor(object sender, RoutedEventArgs e) => vm.Color = (string)((MenuItem)sender).Tag;
    private void OnFont(object sender, RoutedEventArgs e) => vm.FontSize = double.Parse((string)((MenuItem)sender).Tag, CultureInfo.InvariantCulture);
    private void OnFontFamily(object sender, RoutedEventArgs e) => vm.FontFamily = (string)((MenuItem)sender).Tag;
    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (IsCollapsed && e.Key is Key.Enter or Key.Space) { ToggleCollapsed(); e.Handled = true; return; }
        if (Keyboard.Modifiers != ModifierKeys.Control) return;
        if (e.Key == Key.N) { NewNoteRequested?.Invoke(); e.Handled = true; }
        if (e.Key == Key.F) { SearchRequested?.Invoke(); e.Handled = true; }
    }
}
