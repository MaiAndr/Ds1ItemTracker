using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using Ds1ItemTracker.Helpers;
using Ds1ItemTracker.Services;
using Ds1ItemTracker.ViewModels;
using Ds1ItemTracker.Views;

namespace Ds1ItemTracker;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private FloatingOverlay? _floatingOverlay;
    private readonly UpdateChecker _updateChecker = new();

    // ── Win32 global hotkeys ──────────────────────────────────────────────────
    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private const int HotkeyIdNext = 0x3001;
    private const int HotkeyIdPrev = 0x3002;
    private const int WM_HOTKEY    = 0x0312;

    // MOD_ flags for RegisterHotKey
    private const uint MOD_ALT     = 0x0001;
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_SHIFT   = 0x0004;
    private const uint MOD_WIN     = 0x0008;
    private const uint MOD_NOREPEAT = 0x4000;

    private HwndSource? _hwndSource;

    public MainWindow()
    {
        InitializeComponent();
        _vm = new MainViewModel();
        DataContext = _vm;
        TxtVersion.Text = $"v{UpdateChecker.CurrentVersion.ToString(3)}";
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwndSource = PresentationSource.FromVisual(this) as HwndSource;
        _hwndSource?.AddHook(WndProc);
        RegisterGlobalHotkeys();
        _ = CheckForUpdateAsync();
    }

    private async Task CheckForUpdateAsync()
    {
        if (!await _updateChecker.CheckAsync()) return;
        UpdateBannerText.Text =
            $"\u2605  A new version is available: {_updateChecker.LatestTag}  (current: v{UpdateChecker.CurrentVersion})";
        UpdateBanner.Visibility = Visibility.Visible;
    }

    private void UpdateBannerOpen_Click(object sender, RoutedEventArgs e)
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
            _updateChecker.ReleasePage) { UseShellExecute = true });
    }

    private void UpdateBannerDismiss_Click(object sender, RoutedEventArgs e)
        => UpdateBanner.Visibility = Visibility.Collapsed;

    // ── Global hotkey registration ────────────────────────────────────────────

    private void RegisterGlobalHotkeys()
    {
        if (_hwndSource == null) return;
        IntPtr hwnd = _hwndSource.Handle;
        UnregisterHotKey(hwnd, HotkeyIdNext);
        UnregisterHotKey(hwnd, HotkeyIdPrev);
        TryRegisterGlobalHotkey(hwnd, HotkeyIdNext, _vm.Settings.Current.HotkeyNextArea);
        TryRegisterGlobalHotkey(hwnd, HotkeyIdPrev, _vm.Settings.Current.HotkeyPrevArea);
    }

    private void TryRegisterGlobalHotkey(IntPtr hwnd, int id, string hotkeyStr)
    {
        var parsed = HotkeyHelper.Parse(hotkeyStr);
        if (parsed == null) return;
        var (key, mods) = parsed.Value;
        uint fsModifiers = MOD_NOREPEAT;
        if ((mods & ModifierKeys.Control) != 0) fsModifiers |= MOD_CONTROL;
        if ((mods & ModifierKeys.Shift)   != 0) fsModifiers |= MOD_SHIFT;
        if ((mods & ModifierKeys.Alt)     != 0) fsModifiers |= MOD_ALT;
        if ((mods & ModifierKeys.Windows) != 0) fsModifiers |= MOD_WIN;
        uint vk = (uint)KeyInterop.VirtualKeyFromKey(key);
        RegisterHotKey(hwnd, id, fsModifiers, vk);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY)
        {
            switch (wParam.ToInt32())
            {
                case HotkeyIdNext: _vm.NextArea(); handled = true; break;
                case HotkeyIdPrev: _vm.PrevArea(); handled = true; break;
            }
        }
        return IntPtr.Zero;
    }

    private SettingsWindow? _settingsWindow;

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        // Bring existing instance to front instead of opening a second one
        if (_settingsWindow != null)
        {
            _settingsWindow.Activate();
            return;
        }

        int oldPort = _vm.Settings.Current.OverlayPort;
        _settingsWindow = new SettingsWindow(_vm.Settings.Current, _vm) { Owner = this };

        _settingsWindow.Saved += () =>
        {
            _vm.Settings.Save();
            RegisterGlobalHotkeys();
            if (_vm.Settings.Current.OverlayPort != oldPort)
                _vm.RestartOverlay(_vm.Settings.Current.OverlayPort);
        };

        _settingsWindow.Closed += (_, _) => _settingsWindow = null;

        _settingsWindow.Show();
    }

    // ── Window lifecycle ──────────────────────────────────────────────────────

    protected override void OnClosed(EventArgs e)
    {
        if (_hwndSource != null)
        {
            UnregisterHotKey(_hwndSource.Handle, HotkeyIdNext);
            UnregisterHotKey(_hwndSource.Handle, HotkeyIdPrev);
            _hwndSource.RemoveHook(WndProc);
        }
        _floatingOverlay?.Close();
        _vm.Dispose();
        base.OnClosed(e);
    }

    private void TakeSnapshot_Click(object sender, RoutedEventArgs e)
        => _vm.TakeSnapshot();

    private void DetectChanges_Click(object sender, RoutedEventArgs e)
        => _vm.DetectChanges();

    private void ClearChanges_Click(object sender, RoutedEventArgs e)
        => _vm.DetectedChanges.Clear();

    private void CopyFlagId_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.Tag is int flagId)
            _vm.CopyFlagId(flagId);
    }

    private void AddToDatabase_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.Tag is int flagId)
            _vm.AddItemToDatabase(flagId, this);
    }

    private void Diagnostics_Click(object sender, RoutedEventArgs e)
        => _vm.ShowDiagnostics();

    private void Reconnect_Click(object sender, RoutedEventArgs e)
        => _vm.Reconnect();

    private void RemoveItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.MenuItem mi &&
            mi.DataContext is Ds1ItemTracker.Models.ItemLocation item)
        {
            var result = System.Windows.MessageBox.Show(
                $"Remove \"{item.Name}\" from the list?\nThis will update items.json.",
                "Remove Item", System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Question);
            if (result == System.Windows.MessageBoxResult.Yes)
                _vm.RemoveItem(item);
        }
    }

    private void MarkVerified_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.MenuItem mi &&
            mi.DataContext is Ds1ItemTracker.Models.ItemLocation item)
        {
            _vm.MarkVerified(item);
        }
    }

    private void OverlayUrl_Click(object sender, RoutedEventArgs e)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(_vm.OverlayUrl) { UseShellExecute = true }); }
        catch { System.Windows.Clipboard.SetText(_vm.OverlayUrl); }
    }

    private void FloatOverlay_Click(object sender, RoutedEventArgs e)
    {
        if (_floatingOverlay == null || !_floatingOverlay.IsLoaded)
        {
            _floatingOverlay = new FloatingOverlay(_vm) { Owner = this };
            _floatingOverlay.Show();
        }
        else if (_floatingOverlay.IsVisible)
        {
            _floatingOverlay.Hide();
        }
        else
        {
            _floatingOverlay.Show();
        }
    }

    private void ApplyRandomizer_Click(object sender, RoutedEventArgs e)
        => _vm.ApplyRandomizer(this);

    private void RefreshParam_Click(object sender, RoutedEventArgs e)
        => _vm.RefreshLatestParam();

    private void RevertRandomizer_Click(object sender, RoutedEventArgs e)
    {
        var r = MessageBox.Show(
            "Revert to default items.json?\nThis will remove the randomizer flag remapping.",
            "Revert to Default", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (r == MessageBoxResult.Yes)
            _vm.RevertToDefault();
    }
}
