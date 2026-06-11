using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using Ds1ItemTracker.Models;
using Ds1ItemTracker.Services;
using Ds1ItemTracker.Views;

namespace Ds1ItemTracker.ViewModels;

public sealed class FlagChange
{
    public int    FlagId   { get; init; }
    public string StateStr => "SET";
}

public sealed class MainViewModel : INotifyPropertyChanged, IDisposable
{
    // ── Services ─────────────────────────────────────────────────────────────
    private readonly MemoryService   _memory  = new();
    private readonly GameFlagService _flags;
    private readonly DispatcherTimer _pollTimer;
    private          OverlayServer   _overlay;
    public  readonly SettingsService Settings;

    // ── items.json paths ──────────────────────────────────────────────────────
    // Always walk up to the .csproj folder first so source is always updated.
    // The exe-side copy (bin/Release/…) is kept in sync as a secondary write.
    private static readonly string _itemsJsonProjectPath = FindProjectItemsJson();
    private static readonly string _itemsJsonExePath     = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "items.json");
    // ── items.json read path ──────────────────────────────────────────────────
    // Always read from the exe-side copy — that's what the randomizer writes to.
    // Fall back to the project path if the exe-side doesn't exist yet (first run).
    private static string GetReadPath() =>
        File.Exists(_itemsJsonExePath) ? _itemsJsonExePath : _itemsJsonProjectPath;

    private static string FindProjectItemsJson()
    {
        string? dir = AppDomain.CurrentDomain.BaseDirectory;
        for (int i = 0; i < 10 && dir != null; i++)
        {
            if (Directory.GetFiles(dir, "*.csproj").Length > 0)
                return Path.Combine(dir, "items.json");
            dir = Path.GetDirectoryName(dir);
        }
        return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "items.json");
    }

    // ── Save file analyser (own view model, data-bound via SaveFile property) ─
    public SaveFileViewModel SaveFile { get; } = new();

    // ── Item data ─────────────────────────────────────────────────────────────
    public ObservableCollection<ItemArea> Areas { get; } = new();

    private ItemArea? _selectedArea;
    public ItemArea? SelectedArea
    {
        get => _selectedArea;
        set { _selectedArea = value; OnPropertyChanged(); PushOverlayState(); }
    }

    // ── Status ────────────────────────────────────────────────────────────────
    private string _statusText  = "Not connected";
    private bool   _isConnected;

    public string StatusText
    {
        get => _statusText;
        private set { _statusText = value; OnPropertyChanged(); }
    }

    public bool IsConnected
    {
        get => _isConnected;
        private set { _isConnected = value; OnPropertyChanged(); }
    }

    private string _lastUpdate = "—";
    public string LastUpdate
    {
        get => _lastUpdate;
        private set { _lastUpdate = value; OnPropertyChanged(); }
    }

    public string OverlayUrl => _overlay.Url;

    /// <summary>Restart the overlay HTTP server on a new port (called after settings change).</summary>
    public void RestartOverlay(int port)
    {
        _overlay.Dispose();
        _overlay = new OverlayServer(port);
        _overlay.Start();
        OnPropertyChanged(nameof(OverlayUrl));
    }

#if DEBUG
    public bool IsDebugBuild => true;
#else
    public bool IsDebugBuild => false;
#endif

    private int _overlayBgOpacity = 55; // 0–100 percent
    public int OverlayBgOpacity
    {
        get => _overlayBgOpacity;
        set
        {
            _overlayBgOpacity = Math.Clamp(value, 0, 100);
            OnPropertyChanged();
            OnPropertyChanged(nameof(OverlayWindowOpacity));
            OnPropertyChanged(nameof(OverlayCardBackground));
            PushOverlayState();
            Settings.Current.OverlayOpacity = _overlayBgOpacity;
            Settings.Save();
        }
    }

    // Window-level opacity for the floating WPF overlay (0.1–1.0)
    public double OverlayWindowOpacity => Math.Max(0.1, _overlayBgOpacity / 100.0);

    // Card background brush driven by the same opacity slider
    public System.Windows.Media.Brush OverlayCardBackground =>
        new System.Windows.Media.SolidColorBrush(
            System.Windows.Media.Color.FromArgb(
                (byte)(Math.Clamp(_overlayBgOpacity * 2.4, 30, 230)),
                13, 13, 13));

    // ── Overall progress ─────────────────────────────────────────────────────
    public int TotalItems    => Areas.Sum(a => a.TotalItems);
    public int TotalPickedUp => Areas.Sum(a => a.PickedUpCount);
    public int TotalUnverified => Areas.Sum(a => a.UnverifiedCount);
    public string TotalProgress => TotalUnverified > 0
        ? $"{TotalPickedUp} / {TotalItems}  ({TotalUnverified} ⚠ untracked)"
        : $"{TotalPickedUp} / {TotalItems}";

    // ── Flag scanner ──────────────────────────────────────────────────────────
    // We snapshot the raw ~128 KB flag array and diff raw bytes.
    // Changed bits are reverse-mapped to flag IDs by GameFlagService.
    // This avoids iterating millions of flag IDs (most of which are invalid).

    private byte[]? _flagSnapshot;

    public ObservableCollection<FlagChange> DetectedChanges { get; } = new();

    private string _scannerStatus = "Take a snapshot, pick up an item, then detect changes.";
    public string ScannerStatus
    {
        get => _scannerStatus;
        private set { _scannerStatus = value; OnPropertyChanged(); }
    }

    private bool _canDetect;
    public bool CanDetect
    {
        get => _canDetect;
        private set { _canDetect = value; OnPropertyChanged(); }
    }

    // ── Construction ─────────────────────────────────────────────────────────
    public MainViewModel()
    {
        Settings = new SettingsService(_itemsJsonProjectPath
                       .Replace("items.json", string.Empty).TrimEnd('\\', '/'));

        _flags   = new GameFlagService(_memory);
        _overlay = new OverlayServer(Settings.Current.OverlayPort);
        _overlay.Start();

        // Restore persisted opacity
        _overlayBgOpacity = Settings.Current.OverlayOpacity;

        // Auto-detect game folder if not yet saved
        if (string.IsNullOrEmpty(Settings.Current.GameFolder))
        {
            string? detected = SettingsService.TryDetectGameFolder();
            if (detected != null)
            {
                Settings.Current.GameFolder = detected;
                Settings.Save();
            }
        }
        RefreshLatestParam();

        LoadItemDatabase();

        _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _pollTimer.Tick += OnPollTick;
        _pollTimer.Start();
    }

    // ── Item database ─────────────────────────────────────────────────────────
    private void LoadItemDatabase()
    {
        if (!File.Exists(GetReadPath()))
        {
            StatusText = $"items.json not found at: {GetReadPath()}";
            return;
        }

        try
        {
            string json = File.ReadAllText(GetReadPath());
            var db = JsonSerializer.Deserialize<ItemDatabase>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (db?.Areas == null) return;

            Areas.Clear();
            foreach (var area in db.Areas)
                Areas.Add(area);

            if (Areas.Count > 0)
                SelectedArea = Areas[0];

            OnPropertyChanged(nameof(TotalItems));
        }
        catch (Exception ex)
        {
            StatusText = $"Failed to load items.json: {ex.Message}";
        }
    }

    // ── Polling ───────────────────────────────────────────────────────────────
    private void OnPollTick(object? sender, EventArgs e)
    {
        if (!_memory.IsConnected)
        {
            bool ok = _memory.TryConnect();
            if (!ok)
            {
                IsConnected = false;
                StatusText  = "Waiting for DarkSoulsRemastered.exe…";
                _flags.Reset();
                return;
            }
            IsConnected = true;
            StatusText  = "Connected";
        }

        UpdateAllFlags();
    }

    private void UpdateAllFlags()
    {
        // Only poll items with verified flag IDs; unverified have placeholder IDs
        var allItems = Areas.SelectMany(a => a.Items).Where(i => i.Verified).ToList();
        if (allItems.Count == 0) return;

        var ids    = allItems.Select(i => i.FlagId).Distinct().ToList();
        var result = _flags.ReadFlags(ids);

        if (result.Count == 0)
        {
            // Flag array base could not be resolved — probably in main menu
            IsConnected = false;
            StatusText  = "Connected (waiting for game world to load…)";
            return;
        }

        IsConnected = true;
        StatusText  = "Connected";

        ItemArea? newPickupArea = null;

        foreach (var item in allItems)
        {
            if (result.TryGetValue(item.FlagId, out bool val))
            {
                bool wasPickedUp = item.IsPickedUp;
                item.IsPickedUp = val;

                // First newly-picked-up item this tick → switch to its area
                if (!wasPickedUp && val && newPickupArea == null)
                    newPickupArea = Areas.FirstOrDefault(a => a.Items.Contains(item));
            }
        }

        foreach (var area in Areas)
            area.RefreshProgress();

        if (newPickupArea != null)
            SelectedArea = newPickupArea;

        OnPropertyChanged(nameof(TotalPickedUp));
        OnPropertyChanged(nameof(TotalUnverified));
        OnPropertyChanged(nameof(TotalProgress));

        PushOverlayState();

        LastUpdate = DateTime.Now.ToString("HH:mm:ss");
    }

    // ── Flag Scanner commands ─────────────────────────────────────────────────
    public void TakeSnapshot()
    {
        if (!_memory.IsConnected)
        {
            ScannerStatus = "Not connected — start the game first.";
            return;
        }

        _flagSnapshot = _flags.TakeRawSnapshot();
        if (_flagSnapshot == null)
        {
            ScannerStatus = "Failed to resolve flag array. See Diagnostics for details.";
            CanDetect = false;
            return;
        }

        CanDetect     = true;
        ScannerStatus = $"Snapshot taken ({_flagSnapshot.Length:N0} bytes). Now pick up an item, then click Detect Changes.";
        DetectedChanges.Clear();
    }

    public void DetectChanges()
    {
        if (_flagSnapshot == null)
        {
            ScannerStatus = "No snapshot — take a snapshot first.";
            return;
        }

        byte[]? current = _flags.TakeRawSnapshot();
        if (current == null)
        {
            ScannerStatus = "Failed to read current flags.";
            return;
        }

        var changed = GameFlagService.FindNewlySetFlags(_flagSnapshot, current);

        DetectedChanges.Clear();
        foreach (int flagId in changed)
            DetectedChanges.Add(new FlagChange { FlagId = flagId });

        ScannerStatus = changed.Count == 0
            ? "No new flags detected. Try picking up the item and clicking again."
            : $"Found {changed.Count} newly set flag(s). Click \"Add to Database\" to save each one to items.json.";

        // Do NOT auto-advance _flagSnapshot here.
        // The user can click Detect again to re-check against the same baseline,
        // or Take Snapshot again when ready for the next item.
    }

    public void NextArea()
    {
        if (Areas.Count == 0) return;
        int idx = _selectedArea == null ? -1 : Areas.IndexOf(_selectedArea);
        SelectedArea = Areas[(idx + 1) % Areas.Count];
    }

    public void PrevArea()
    {
        if (Areas.Count == 0) return;
        int idx = _selectedArea == null ? 0 : Areas.IndexOf(_selectedArea);
        SelectedArea = Areas[(idx - 1 + Areas.Count) % Areas.Count];
    }

    public void Reconnect()
    {
        _memory.Disconnect();
        _flags.Reset();
        IsConnected = false;
        StatusText  = "Reconnecting…";
    }

    public void ShowDiagnostics()
    {
        _flags.Reset(); // force a fresh resolution attempt
        _flags.TakeRawSnapshot(); // trigger one resolution cycle (result discarded)
        MessageBox.Show(_flags.DiagInfo,
            "Flag Pointer Diagnostics",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    public void CopyFlagId(int flagId)
    {
        try { Clipboard.SetText(flagId.ToString()); }
        catch { /* clipboard unavailable */ }
    }

    public void AddItemToDatabase(int flagId, Window owner)
    {
        var dialog = new AddItemDialog(flagId, Areas)
        {
            Owner = owner
        };

        if (dialog.ShowDialog() != true) return;

        // Build the new ItemLocation
        var newItem = new ItemLocation
        {
            Id       = $"{dialog.AreaId}_{Slugify(dialog.ItemName)}_{flagId}",
            Name     = dialog.ItemName,
            FlagId   = flagId,
            Verified = true,
            Notes    = dialog.Notes
        };

        ItemArea targetArea;
        if (dialog.IsNewArea)
        {
            targetArea = new ItemArea { Id = dialog.AreaId, Name = dialog.AreaName };
            Areas.Add(targetArea);
        }
        else
        {
            targetArea = Areas.First(a => a.Id == dialog.AreaId);
        }

        targetArea.Items.Add(newItem);
        targetArea.RefreshProgress();
        OnPropertyChanged(nameof(TotalItems));
        OnPropertyChanged(nameof(TotalProgress));

        SaveDatabase();

        ScannerStatus = $"Added \"{newItem.Name}\" (flag {flagId}) to area \"{targetArea.Name}\" and saved to items.json.";
    }

    public void RemoveItem(ItemLocation item)
    {
        var area = Areas.FirstOrDefault(a => a.Items.Contains(item));
        if (area == null) return;
        area.Items.Remove(item);
        area.RefreshProgress();
        // Remove area if now empty
        if (area.Items.Count == 0)
            Areas.Remove(area);
        OnPropertyChanged(nameof(TotalItems));
        OnPropertyChanged(nameof(TotalUnverified));
        OnPropertyChanged(nameof(TotalProgress));
        SaveDatabase();
    }

    public void MarkVerified(ItemLocation item)
    {
        item.Verified = true;
        var area = Areas.FirstOrDefault(a => a.Items.Contains(item));
        area?.RefreshProgress();
        OnPropertyChanged(nameof(TotalItems));
        OnPropertyChanged(nameof(TotalUnverified));
        OnPropertyChanged(nameof(TotalProgress));
        SaveDatabase();
    }

    private void SaveDatabase()
    {
        try
        {
            var db = new ItemDatabase { Areas = Areas.ToList() };
            string json = JsonSerializer.Serialize(db, new JsonSerializerOptions
            {
                WriteIndented        = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            // Write to project source (primary)
            File.WriteAllText(_itemsJsonProjectPath, json);

            // Keep exe-side copy in sync so the running app always has the latest
            if (!string.Equals(_itemsJsonProjectPath, _itemsJsonExePath,
                    StringComparison.OrdinalIgnoreCase))
                File.WriteAllText(_itemsJsonExePath, json);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to save items.json:\n{ex.Message}", "Save Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static string Slugify(string text)
        => System.Text.RegularExpressions.Regex
               .Replace(text.ToLowerInvariant().Trim(), @"[^a-z0-9]+", "_")
               .Trim('_');

    // ── Item Randomizer support ───────────────────────────────────────────────

    private static readonly string _itemsDefaultPath =
        _itemsJsonExePath.Replace("items.json", "items.default.json");
    private static readonly string _itemsDefaultExePath =
        _itemsJsonExePath.Replace("items.json", "items.default.json");

    private bool _randomizerActive;
    public bool RandomizerActive
    {
        get => _randomizerActive;
        private set { _randomizerActive = value; OnPropertyChanged(); OnPropertyChanged(nameof(RandomizerStatusText)); }
    }

    public string RandomizerStatusText => RandomizerActive ? "Randomizer Active" : "Default";

    private string _latestParamFile = string.Empty;
    public string LatestParamFile
    {
        get => _latestParamFile;
        private set { _latestParamFile = value; OnPropertyChanged(); OnPropertyChanged(nameof(ParamFileLabel)); OnPropertyChanged(nameof(CanApplyRandomizer)); }
    }

    public string ParamFileLabel => string.IsNullOrEmpty(_latestParamFile)
        ? "No seed found"
        : System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(_latestParamFile)!) + "\\ItemLotParam.param";

    public bool CanApplyRandomizer => !string.IsNullOrEmpty(_latestParamFile);

    public string GameFolder
    {
        get => Settings.Current.GameFolder;
        set
        {
            Settings.Current.GameFolder = value;
            Settings.Save();
            OnPropertyChanged();
            RefreshLatestParam();
        }
    }

    public void RefreshLatestParam()
    {
        string? found = SettingsService.FindLatestParamFile(Settings.Current.GameFolder);
        LatestParamFile = found ?? string.Empty;
    }

    /// <summary>
    /// Apply the latest detected randomizer param file.
    /// </summary>
    public void ApplyRandomizer(Window owner)
    {
        if (string.IsNullOrEmpty(_latestParamFile))
        {
            MessageBox.Show("No ItemLotParam.param found. Make sure the game folder is correct and a randomizer seed folder exists.",
                "No Param File", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        ApplyRandomizerFromPath(_latestParamFile, owner);
    }

    private void ApplyRandomizerFromPath(string paramPath, Window owner)
    {
        var result = ItemLotParamService.Parse(paramPath);
        if (!result.Success)
        {
            MessageBox.Show($"Failed to parse param file:\n{result.Error}\n\nDiagnostics:\n{result.Diagnostics}",
                "Randomizer Error", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        if (result.Mapping.Count == 0)
        {
            MessageBox.Show("No flag mappings found in param file.\n\nDiagnostics:\n" + result.Diagnostics,
                "Randomizer Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // First application: save the unmodified items.json as the default backup
        EnsureDefaultBackup();

        // Reload from the default backup so we always start from a clean slate
        // (handles re-applying after a different seed)
        string jsonToMap = File.Exists(_itemsDefaultPath)
            ? File.ReadAllText(_itemsDefaultPath)
            : File.ReadAllText(GetReadPath());

        var db = JsonSerializer.Deserialize<ItemDatabase>(jsonToMap,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (db == null)
        {
            MessageBox.Show("Could not read items.json.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        int updated = 0;
        foreach (var area in db.Areas)
        foreach (var item in area.Items)
        {
            // Only remap world-pickup and boss-drop flags (50_000_000+)
            if (item.FlagId >= 50_000_000 && result.Mapping.TryGetValue(item.FlagId, out long newFlag))
            {
                if (newFlag != item.FlagId)
                {
                    item.FlagId = newFlag;
                    updated++;
                }
            }
        }

        // Save the remapped database — write to exe folder only
        // (the project source items.json should stay as the clean default)
        string newJson = JsonSerializer.Serialize(db, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
        File.WriteAllText(_itemsJsonExePath, newJson);

        // Reload
        LoadItemDatabase();
        RandomizerActive = true;

        MessageBox.Show(
            $"Randomizer applied.\n\n" +
            $"• {updated} item location flags updated\n" +
            $"• {result.Mapping.Count} total rows read from param\n\n" +
            $"The original items.json is backed up as items.default.json.\n" +
            $"Use \"Revert to Default\" to restore it.\n\n" +
            $"--- Parse Diagnostics ---\n{result.Diagnostics}",
            "Randomizer Applied", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    public void RevertToDefault()
    {
        if (!File.Exists(_itemsDefaultPath))
        {
            MessageBox.Show("No backup found (items.default.json). Already using default.",
                "Revert", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        string json = File.ReadAllText(_itemsDefaultPath);
        // Restore to exe folder only
        File.WriteAllText(_itemsJsonExePath, json);

        LoadItemDatabase();
        RandomizerActive = false;
    }

    private void EnsureDefaultBackup()
    {
        if (File.Exists(_itemsDefaultPath)) return;
        // Back up the current exe-side items.json
        string source = File.Exists(_itemsJsonExePath) ? _itemsJsonExePath : _itemsJsonProjectPath;
        if (!File.Exists(source)) return;
        File.WriteAllText(_itemsDefaultPath, File.ReadAllText(source));
    }

    // ── Overlay state push ───────────────────────────────────────────────────
    private void PushOverlayState()
    {
        if (_selectedArea == null) return;
        var items = _selectedArea.Items
            .Where(i => i.Verified)
            .Select(i => new OverlayItem(i.Name, i.IsPickedUp))
            .ToList();
        _overlay.UpdateState(new OverlaySnapshot
        {
            AreaName  = _selectedArea.Name,
            PickedUp  = _selectedArea.PickedUpCount,
            Total     = _selectedArea.TotalItems,
            BgOpacity = _overlayBgOpacity / 100.0,
            Items     = items
        });
    }

    // ── INotifyPropertyChanged ────────────────────────────────────────────────
    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public void Dispose()
    {
        _pollTimer.Stop();
        _memory.Dispose();
        _overlay.Dispose();
    }
}
