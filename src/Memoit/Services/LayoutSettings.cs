using System.Text.Json;
using System.Text.Json.Serialization;

namespace Memoit.Services;

public enum LayoutShape { Grid, Horizontal, Vertical }
public enum LayoutSort { Created, Color, Title }

public sealed record MonitorLayout
{
    public double X { get; init; } = 8;
    public double Y { get; init; } = 8;
    public double Gap { get; init; } = 8;
    public int Columns { get; init; }
    public bool IncludeExpanded { get; init; }
    public ExpandedCorner ExpandedCorner { get; init; } = ExpandedCorner.TopRight;
    public LayoutShape Shape { get; init; } = LayoutShape.Grid;
    public LayoutSort Sort { get; init; } = LayoutSort.Created;

    public void Validate()
    {
        if (!double.IsFinite(X) || X < 0 || !double.IsFinite(Y) || Y < 0
            || !double.IsFinite(Gap) || Gap < 0 || Gap > 24 || Columns < 0 || Columns > 30
            || !Enum.IsDefined(Shape) || !Enum.IsDefined(Sort) || !Enum.IsDefined(ExpandedCorner))
            throw new InvalidDataException("모니터 타일 배치 설정이 올바르지 않습니다.");
    }
}

public sealed record LayoutSettings
{
    public int Version { get; init; } = 5;
    public bool AutoArrange { get; init; }
    public bool NeedsManualArrangementNotice { get; init; }
    public bool NeedsShortcutSimplificationNotice { get; init; }
    public bool SortByColor { get; init; }
    public Dictionary<string, MonitorLayout> Monitors { get; init; } = [];
    public Dictionary<string, List<Guid>> ArrangementOrder { get; init; } = [];
    public Dictionary<string, string> Hotkeys { get; init; } = HotkeyDefaults.Create();
    public string? SelectedMonitor { get; init; }
    public double? PanelLeft { get; init; }
    public double? PanelTop { get; init; }
    public double? OverlayLeft { get; init; }
    public double? OverlayTop { get; init; }
    public double OverlayOpacity { get; init; } = .85;
    public bool OverlayTopmost { get; init; }
    public string? OverlayColor { get; init; }
    public bool OverlayVisible { get; init; }

    public MonitorLayout GetMonitor(string id) => Monitors.TryGetValue(id, out var layout)
        ? layout : new MonitorLayout { Sort = SortByColor ? LayoutSort.Color : LayoutSort.Created };

    public void Validate()
    {
        if (!double.IsFinite(OverlayOpacity) || OverlayOpacity < .3 || OverlayOpacity > 1
            || OverlayLeft.HasValue && !double.IsFinite(OverlayLeft.Value)
            || OverlayTop.HasValue && !double.IsFinite(OverlayTop.Value)
            || OverlayColor is not null && !NoteColors.Values.Any(c => NoteColors.Matches(c, OverlayColor)))
            throw new InvalidDataException("미니 패널 위치 또는 투명도가 올바르지 않습니다.");
        if (Version != 5) throw new InvalidDataException("지원하지 않는 타일 설정 버전입니다.");
        if (Monitors is null || Hotkeys is null || ArrangementOrder is null || PanelLeft.HasValue && !double.IsFinite(PanelLeft.Value)
            || PanelTop.HasValue && !double.IsFinite(PanelTop.Value))
            throw new InvalidDataException("타일 설정 값이 올바르지 않습니다.");
        foreach (var pair in Monitors)
        {
            if (string.IsNullOrWhiteSpace(pair.Key) || pair.Value is null) throw new InvalidDataException("모니터 설정이 올바르지 않습니다.");
            pair.Value.Validate();
        }
        foreach (var pair in Hotkeys)
            if (string.IsNullOrWhiteSpace(pair.Key) || pair.Value is null) throw new InvalidDataException("단축키 설정이 올바르지 않습니다.");
        if (Hotkeys.Keys.Any(key => !HotkeyDefaults.Labels.ContainsKey(key)))
            throw new InvalidDataException("지원하지 않는 단축키 동작이 설정에 있습니다.");
        foreach (var pair in ArrangementOrder)
            if (string.IsNullOrWhiteSpace(pair.Key) || pair.Value is null || pair.Value.Contains(Guid.Empty)
                || pair.Value.Distinct().Count() != pair.Value.Count)
                throw new InvalidDataException("모니터별 메모 순서가 올바르지 않습니다.");
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public static LayoutSettings Load(string filePath)
    {
        if (!File.Exists(filePath)) return new LayoutSettings();
        string json = File.ReadAllText(filePath);
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("타일 설정 파일은 JSON 객체여야 합니다.");
        ValidateJsonNames(document.RootElement);
        var settings = JsonSerializer.Deserialize<LayoutSettings>(json, JsonOptions)
            ?? throw new InvalidDataException("타일 설정을 읽지 못했습니다.");
        bool missingVersion = !document.RootElement.TryGetProperty("Version", out _);
        bool migrating = missingVersion || settings.Version is >= 1 and <= 4;
        if (!migrating && settings.Version != 5)
            throw new InvalidDataException("지원하지 않는 타일 설정 버전입니다.");
        if (settings.Hotkeys is null || settings.Hotkeys.Any(p => string.IsNullOrWhiteSpace(p.Key) || p.Value is null))
            throw new InvalidDataException("단축키 설정이 올바르지 않습니다.");

        if (!migrating && settings.Hotkeys.Keys.Any(key => !HotkeyDefaults.Labels.ContainsKey(key)))
            throw new InvalidDataException("지원하지 않는 단축키 동작이 설정에 있습니다.");

        // Retired gestures cannot silently acquire a different meaning through a new default.
        var retiredGestures = settings.Hotkeys.Where(p => !HotkeyDefaults.Labels.ContainsKey(p.Key))
            .Select(p => HotkeyService.Normalize(p.Value)).ToHashSet(StringComparer.Ordinal);
        var hotkeys = settings.Hotkeys.Where(p => HotkeyDefaults.Labels.ContainsKey(p.Key))
            .ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
        if (migrating && !settings.Hotkeys.ContainsKey("Search") && settings.Hotkeys.TryGetValue("ShowList", out var oldList))
            hotkeys["Search"] = oldList;
        var used = hotkeys.Values.Select(HotkeyService.Normalize).ToHashSet(StringComparer.Ordinal);
        used.UnionWith(retiredGestures);
        foreach (var pair in HotkeyDefaults.Create())
            if (!hotkeys.ContainsKey(pair.Key))
            {
                var gesture = used.Contains(pair.Value) ? "" : pair.Value;
                hotkeys.Add(pair.Key, gesture);
                used.Add(gesture);
            }
        settings = settings with
        {
            Version = 5,
            AutoArrange = false,
            NeedsManualArrangementNotice = settings.NeedsManualArrangementNotice || missingVersion || settings.Version is 1 or 2,
            NeedsShortcutSimplificationNotice = settings.NeedsShortcutSimplificationNotice || migrating,
            Hotkeys = hotkeys
        };
        settings.Validate();
        if (migrating)
        {
            // Load does not overwrite the source. Preserve it before any caller can save v5.
            string backup = filePath + ".pre-v5.bak";
            if (!File.Exists(backup)) File.Copy(filePath, backup);
        }
        return settings;
    }

    private static void ValidateJsonNames(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object) return;
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!names.Add(property.Name)) throw new InvalidDataException("타일 설정에 중복된 항목이 있습니다.");
            ValidateJsonNames(property.Value);
        }
    }

    public void Save(string filePath)
    {
        Validate();
        string destination = Path.GetFullPath(filePath);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(file, this, JsonOptions);
                file.Flush(true);
            }
            File.Move(temporary, destination, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
