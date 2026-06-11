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

    [JsonPropertyName("gameFolder")]
    public string GameFolder { get; set; } = string.Empty;

    [JsonPropertyName("hotkeyNextArea")]
    public string HotkeyNextArea { get; set; } = "Ctrl+Right";

    [JsonPropertyName("hotkeyPrevArea")]
    public string HotkeyPrevArea { get; set; } = "Ctrl+Left";

    [JsonPropertyName("overlayPort")]
    public int OverlayPort { get; set; } = 7373;
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

    // ── Game folder helpers ───────────────────────────────────────────────────

    /// <summary>
    /// Tries to auto-detect the DS1R install folder from the default Steam library.
    /// </summary>
    public static string? TryDetectGameFolder()
    {
        string progX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        string candidate = Path.Combine(progX86, "Steam", "steamapps", "common", "DARK SOULS REMASTERED");
        return Directory.Exists(candidate) ? candidate : null;
    }

    /// <summary>
    /// Searches the game folder for the most-recently-created randomizer seed
    /// folder (name starts with "random-seed") and returns the path to its
    /// ItemLotParam.param file, or null if none is found.
    /// </summary>
    public static string? FindLatestParamFile(string gameFolder)
    {
        if (string.IsNullOrEmpty(gameFolder) || !Directory.Exists(gameFolder))
            return null;

        var seedDir = new DirectoryInfo(gameFolder)
            .GetDirectories("random-seed*", SearchOption.TopDirectoryOnly)
            .OrderByDescending(d => d.CreationTimeUtc)
            .FirstOrDefault();

        if (seedDir == null) return null;

        string param = Path.Combine(seedDir.FullName, "ItemLotParam.param");
        return File.Exists(param) ? param : null;
    }
}
