using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Memoit.Views;

public sealed class FontPickerWindow : Window
{
    private readonly FontFamily[] fonts = Fonts.SystemFontFamilies.OrderBy(f => f.Source).ToArray();
    private readonly ListBox list = new() { DisplayMemberPath = "Source", MinHeight = 80 };
    private readonly TextBlock sample = new() { Text = "한글 메모 · 가나다  Aa 123", FontSize = 22, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 12) };
    public string? SelectedFont => (list.SelectedItem as FontFamily)?.Source;

    public FontPickerWindow(string current)
    {
        Title = "설치된 글꼴"; Width = 380; Height = 480;
        MaxHeight = SystemParameters.WorkArea.Height; MaxWidth = SystemParameters.WorkArea.Width;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FontFamily = new FontFamily("Malgun Gothic"); ShowInTaskbar = false;
        var grid = new Grid { Margin = new Thickness(16) };
        foreach (var height in new[] { GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto, GridLength.Auto })
            grid.RowDefinitions.Add(new RowDefinition { Height = height });
        var search = new TextBox { Margin = new Thickness(0, 0, 0, 10), Padding = new Thickness(6), ToolTip = "설치된 글꼴 이름 검색" };
        AutomationProperties.SetName(search, "글꼴 검색"); AutomationProperties.SetName(list, "설치된 글꼴 목록");
        grid.Children.Add(search); Grid.SetRow(list, 1); grid.Children.Add(list);
        Grid.SetRow(sample, 2); grid.Children.Add(sample);
        var apply = new Button { Content = "선택", IsDefault = true, MinWidth = 80, Padding = new Thickness(8), HorizontalAlignment = HorizontalAlignment.Right };
        Grid.SetRow(apply, 3); grid.Children.Add(apply); Content = grid;
        list.ItemsSource = fonts; list.SelectedItem = fonts.FirstOrDefault(f => f.Source.Equals(current, StringComparison.OrdinalIgnoreCase));
        search.TextChanged += (_, _) => list.ItemsSource = fonts.Where(f => f.Source.Contains(search.Text.Trim(), StringComparison.CurrentCultureIgnoreCase)).ToArray();
        list.SelectionChanged += (_, _) => { apply.IsEnabled = SelectedFont is not null; sample.FontFamily = list.SelectedItem as FontFamily ?? FontFamily; };
        apply.IsEnabled = SelectedFont is not null;
        sample.FontFamily = list.SelectedItem as FontFamily ?? FontFamily;
        apply.Click += (_, _) => { if (SelectedFont is not null) DialogResult = true; };
        list.MouseDoubleClick += (_, _) => { if (SelectedFont is not null) DialogResult = true; };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { DialogResult = false; e.Handled = true; } };
        Loaded += (_, _) => search.Focus();
    }
}
