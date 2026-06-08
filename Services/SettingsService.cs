namespace Ds1ItemTracker.Services;

using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

public sealed class AppSettings
{
    [JsonPropertyName("overlayOpacity")]
    public int OverlayOpacity { get; set; } = 55;

    [JsonPropertyName("floatLeft")]
    public double FloatLeft { get; set; } = 20;

    [JsonPropertyName("floatTop")]
    public double FloatTop { get; set; } = 20;
}

public sealed class SettingsService
{
    private static readonly JsonSerializerOptions _opts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _projectPath;
    private readonly string _exePath;

    public AppSettings Current { get; private set; } = new();

    public SettingsService(string projectRoot)
    {
        _projectPath = Path.Combine(projectRoot, "settings.json");
        _exePath     = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json");
        Load();
    }

    private void Load()
    {
        // Try project root first, then exe dir
        foreach (var path in new[] { _projectPath, _exePath })
        {
            if (!File.Exists(path)) continue;
            try
            {
                var loaded = JsonSerializer.Deserialize<AppSettings>(
                    File.ReadAllText(path), _opts);
                if (loaded != null) { Current = loaded; return; }
            }
            catch { }
        }
    }

    public void Save()
    {
        string json = JsonSerializer.Serialize(Current, _opts);
        try { File.WriteAllText(_projectPath, json); } catch { }
        if (!string.Equals(_projectPath, _exePath,
                StringComparison.OrdinalIgnoreCase))
        {
            try { File.WriteAllText(_exePath, json); } catch { }
        }
    }
}
