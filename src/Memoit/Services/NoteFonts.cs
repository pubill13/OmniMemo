using System.Windows.Media;

namespace Memoit.Services;

public static class NoteFonts
{
    public static readonly (string Name, string Label)[] Bundled =
    [
        ("Pretendard", "Pretendard · 프리텐다드"),
        ("NanumGothic", "나눔고딕"),
        ("NanumMyeongjo", "나눔명조"),
        ("Nanum Pen", "나눔손글씨 펜")
    ];
    public static readonly (string Name, string Label)[] Familiar =
    [
        ("Malgun Gothic", "맑은 고딕"), ("Gulim", "굴림"), ("Segoe UI", "Segoe UI"),
        ("Arial", "Arial"), ("Consolas", "Consolas"), ("Times New Roman", "Times New Roman")
    ];
    private static readonly Lazy<HashSet<string>> Installed = new(() =>
        Fonts.SystemFontFamilies.Select(f => f.Source).ToHashSet(StringComparer.OrdinalIgnoreCase));

    public static FontFamily Create(string name) => new(new Uri("pack://application:,,,/OmniMemo;component/"), Resolve(name));

    public static string Resolve(string name)
    {
        var bundled = Bundled.FirstOrDefault(f => f.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (bundled.Name is not null)
            return "./Assets/Fonts/#" + bundled.Name;
        return Installed.Value.Contains(name) ? name : "Malgun Gothic";
    }
}
