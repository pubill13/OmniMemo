namespace Memoit.Services;

public static class HotkeyDefaults
{
    public static IReadOnlyDictionary<string, string> Labels { get; } = new Dictionary<string, string>
    {
        ["NewNote"] = "새 메모",
        ["Search"] = "목록·검색",
        ["ToggleCurrent"] = "현재 메모 접기 / 펼치기",
        ["ToggleVisibility"] = "모두 숨기기 / 보이기",
        ["ToggleCollapsed"] = "모두 접기 / 모두 펼치기",
        ["Arrange"] = "접어서 정돈",
        ["ArrangeExpanded"] = "펼쳐서 정돈",
        ["ToggleOverlay"] = "패널 열기 / 닫기"
    };

    public static Dictionary<string, string> Create() => new(StringComparer.Ordinal)
    {
        ["NewNote"] = "Ctrl+Alt+Shift+N",
        ["Search"] = "Ctrl+Alt+Shift+F",
        ["ToggleCurrent"] = "Ctrl+Shift+Space",
        ["ToggleVisibility"] = "Ctrl+Alt+Shift+H",
        ["ToggleCollapsed"] = "Ctrl+Alt+Shift+C",
        ["Arrange"] = "",
        ["ArrangeExpanded"] = "",
        ["ToggleOverlay"] = "Ctrl+Alt+Shift+P"
    };
}
