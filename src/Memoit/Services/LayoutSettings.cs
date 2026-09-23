using System.Text.Json;
using System.Text.Json.Serialization;

namespace Memoit.Services;

public sealed record LayoutSettings
{
    public bool AutoArrange { get; init; }
    public bool SortByColor { get; init; }

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
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (!names.Add(property.Name)) throw new InvalidDataException("타일 설정에 중복된 항목이 있습니다.");
        }
        return JsonSerializer.Deserialize<LayoutSettings>(json, JsonOptions)
            ?? throw new InvalidDataException("타일 설정을 읽지 못했습니다.");
    }

    public void Save(string filePath)
    {
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
