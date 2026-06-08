using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using Ds1ItemTracker.Services;

namespace Ds1ItemTracker.ViewModels;

public sealed class SaveFileViewModel : INotifyPropertyChanged
{
    private readonly SaveFileService _svc = new();

    // ── Save file path ────────────────────────────────────────────────────────
    private string _savePath = string.Empty;
    public string SavePath
    {
        get => _savePath;
        set { _savePath = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasPath)); }
    }

    public bool HasPath => !string.IsNullOrEmpty(_savePath);

    // ── Status ────────────────────────────────────────────────────────────────
    private string _status = "Browse to your DRAKS0005.sl2 file, or click Auto-Detect.";
    public string Status
    {
        get => _status;
        private set { _status = value; OnPropertyChanged(); }
    }

    private bool _hasSnapshot;
    public bool HasSnapshot
    {
        get => _hasSnapshot;
        private set { _hasSnapshot = value; OnPropertyChanged(); }
    }

    // ── Results ────────────────────────────────────────────────────────────────
    public ObservableCollection<SaveFileService.ByteRegion> ChangedRegions { get; } = new();

    private SaveFileService.ByteRegion? _selectedRegion;
    public SaveFileService.ByteRegion? SelectedRegion
    {
        get => _selectedRegion;
        set { _selectedRegion = value; OnPropertyChanged(); }
    }

    // ── Commands ──────────────────────────────────────────────────────────────
    public void AutoDetect()
    {
        string? path = SaveFileService.AutoDetectSavePath();
        if (path == null)
        {
            Status = "Could not auto-detect save file. Use Browse to locate DRAKS0005.sl2.";
            return;
        }
        SavePath = path;
        Status   = $"Found: {path}";
    }

    public void Browse(Window owner)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title       = "Open DS1R Save File",
            Filter      = "SL2 Save Files (*.sl2)|*.sl2|All Files (*.*)|*.*",
            FileName    = "DRAKS0005.sl2",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)
        };
        if (dlg.ShowDialog(owner) == true)
        {
            SavePath = dlg.FileName;
            Status   = $"Loaded: {SavePath}";
        }
    }

    public void TakeSnapshot()
    {
        if (!HasPath) { Status = "No file selected."; return; }

        var (ok, error, size) = _svc.TakeSnapshot(_savePath);
        if (!ok)
        {
            Status = $"Snapshot failed: {error}";
            HasSnapshot = false;
            return;
        }
        HasSnapshot = true;
        ChangedRegions.Clear();
        SelectedRegion = null;
        Status = $"Snapshot taken ({size:N0} bytes). Now save in-game (or wait for autosave), then click Detect Changes.";
    }

    public void DetectChanges()
    {
        if (!HasPath)    { Status = "No file selected."; return; }
        if (!HasSnapshot) { Status = "Take a snapshot first."; return; }

        var (regions, error) = _svc.Diff(_savePath);

        ChangedRegions.Clear();
        SelectedRegion = null;

        if (!string.IsNullOrEmpty(error))
        {
            Status = $"Diff failed: {error}";
            return;
        }

        if (regions.Count == 0)
        {
            Status = "No changes detected. Make sure the game has saved since the snapshot (look for the save icon in-game).";
            return;
        }

        foreach (var r in regions)
            ChangedRegions.Add(r);

        Status = $"Found {regions.Count} changed region(s). Select one to see the hex diff.";
    }

    public void ClearSnapshot()
    {
        _svc.ClearSnapshot();
        HasSnapshot = false;
        ChangedRegions.Clear();
        SelectedRegion = null;
        Status = "Snapshot cleared.";
    }

    public void CopyOffset()
    {
        if (SelectedRegion == null) return;
        try { Clipboard.SetText(SelectedRegion.OffsetHex); } catch { }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
