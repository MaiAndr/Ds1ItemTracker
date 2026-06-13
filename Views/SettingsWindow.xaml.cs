using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Ds1ItemTracker.Helpers;
using Ds1ItemTracker.Services;
using Ds1ItemTracker.ViewModels;
using WinForms = System.Windows.Forms;

namespace Ds1ItemTracker.Views;

public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;
    private readonly MainViewModel _vm;

    // Snapshot original values for Cancel
    private readonly string _origNextHotkey;
    private readonly string _origPrevHotkey;
    private readonly int    _origPort;
    private readonly int    _origFontSize;
    private readonly string _origFontColor;
    private readonly int    _origFontOpacity;
    private readonly string _origBgColor;
    private readonly int    _origBgOpacity;
    private readonly int    _origColumns;

    // Raw key strings built while the user types
    private string _nextRaw;
    private string _prevRaw;

    private bool _loading = true;

    public SettingsWindow(AppSettings settings, MainViewModel vm)
    {
        InitializeComponent();
        _settings = settings;
        _vm = vm;

        // Snapshot for cancel
        _origNextHotkey  = settings.HotkeyNextArea;
        _origPrevHotkey  = settings.HotkeyPrevArea;
        _origPort        = settings.OverlayPort;
        _origFontSize    = settings.OverlayFontSize;
        _origFontColor   = settings.OverlayFontColor;
        _origFontOpacity = settings.OverlayFontOpacity;
        _origBgColor     = settings.OverlayBgColor;
        _origBgOpacity   = settings.OverlayBgOpacityPercent;
        _origColumns     = settings.OverlayColumns;

        _nextRaw = settings.HotkeyNextArea;
        _prevRaw = settings.HotkeyPrevArea;

        LoadOverlayControls(settings);

        _loading = false;
    }

    private void LoadOverlayControls(AppSettings s)
    {
        _loading = true;
        TxtNext.Text            = HotkeyHelper.Display(_nextRaw);
        TxtPrev.Text            = HotkeyHelper.Display(_prevRaw);
        TxtPort.Text            = s.OverlayPort.ToString();
        SliderFontSize.Value    = s.OverlayFontSize;
        SliderFontOpacity.Value = s.OverlayFontOpacity;
        SliderBgOpacity.Value   = s.OverlayBgOpacityPercent;
        TxtColumns.Text         = s.OverlayColumns.ToString();
        if (TxtFontSizeVal    != null) TxtFontSizeVal.Text    = s.OverlayFontSize + " px";
        if (TxtFontOpacityVal != null) TxtFontOpacityVal.Text = s.OverlayFontOpacity + "%";
        if (TxtBgOpacityVal   != null) TxtBgOpacityVal.Text   = s.OverlayBgOpacityPercent + "%";
        ApplyColorSwatch(FontColorSwatch, s.OverlayFontColor);
        ApplyColorSwatch(BgColorSwatch,   s.OverlayBgColor);
        _loading = false;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static void ApplyColorSwatch(System.Windows.Controls.Border swatch, string hex)
    {
        if (hex.StartsWith('#') && hex.Length == 7
            && byte.TryParse(hex[1..3], System.Globalization.NumberStyles.HexNumber, null, out byte r)
            && byte.TryParse(hex[3..5], System.Globalization.NumberStyles.HexNumber, null, out byte g)
            && byte.TryParse(hex[5..7], System.Globalization.NumberStyles.HexNumber, null, out byte b))
        {
            swatch.Background = new SolidColorBrush(Color.FromRgb(r, g, b));
        }
    }

    private static string ColorToHex(System.Drawing.Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    private static System.Drawing.Color HexToDrawingColor(string hex)
    {
        if (hex.StartsWith('#') && hex.Length == 7)
        {
            if (byte.TryParse(hex[1..3], System.Globalization.NumberStyles.HexNumber, null, out byte r)
             && byte.TryParse(hex[3..5], System.Globalization.NumberStyles.HexNumber, null, out byte g)
             && byte.TryParse(hex[5..7], System.Globalization.NumberStyles.HexNumber, null, out byte b))
                return System.Drawing.Color.FromArgb(r, g, b);
        }
        return System.Drawing.Color.Black;
    }

    // ── Key capture ───────────────────────────────────────────────────────────

    private void HotkeyBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;

        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift
                or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin)
            return;

        if (key == Key.Escape)
        {
            var box = (TextBox)sender;
            if (box == TxtNext) { _nextRaw = string.Empty; TxtNext.Text = "(none)"; }
            else                 { _prevRaw = string.Empty; TxtPrev.Text = "(none)"; }
            return;
        }

        ModifierKeys mods = Keyboard.Modifiers;
        string raw = HotkeyHelper.Format(key, mods);
        string display = HotkeyHelper.Display(raw);

        if (sender == TxtNext) { _nextRaw = raw; TxtNext.Text = display; }
        else                    { _prevRaw = raw; TxtPrev.Text = display; }
    }

    private void HotkeyBox_GotFocus(object sender, RoutedEventArgs e)
    {
        var box = (TextBox)sender;
        box.BorderBrush = new SolidColorBrush(Color.FromRgb(0xB8, 0x96, 0x0C));
        if (box == TxtNext) TxtNextHint.Text = "Press a key combination…";
        else                 TxtPrevHint.Text = "Press a key combination…";
    }

    private void HotkeyBox_LostFocus(object sender, RoutedEventArgs e)
    {
        var box = (TextBox)sender;
        box.BorderBrush = new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x3A));
        if (box == TxtNext) TxtNextHint.Text = "Click to capture…";
        else                 TxtPrevHint.Text = "Click to capture…";
    }

    // ── Live preview handlers ─────────────────────────────────────────────────

    private void SliderFontSize_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        int v = (int)SliderFontSize.Value;
        if (TxtFontSizeVal != null) TxtFontSizeVal.Text = v + " px";
        if (!_loading) _vm.OverlayFontSize = v;
    }

    private void SliderFontOpacity_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        int v = (int)SliderFontOpacity.Value;
        if (TxtFontOpacityVal != null) TxtFontOpacityVal.Text = v + "%";
        if (!_loading) _vm.OverlayFontOpacity = v;
    }

    private void SliderBgOpacity_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        int v = (int)SliderBgOpacity.Value;
        if (TxtBgOpacityVal != null) TxtBgOpacityVal.Text = v + "%";
        if (!_loading) _vm.OverlayBgOpacityPercent = v;
    }

    private void TxtColumns_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_loading && int.TryParse(TxtColumns.Text.Trim(), out int cols) && cols is >= 1 and <= 8)
            _vm.OverlayColumns = cols;
    }

    private void PickFontColor_Click(object sender, RoutedEventArgs e)
    {
        using var dlg = new WinForms.ColorDialog
        {
            FullOpen = true,
            Color = HexToDrawingColor(_settings.OverlayFontColor)
        };
        if (dlg.ShowDialog() == WinForms.DialogResult.OK)
        {
            string hex = ColorToHex(dlg.Color);
            ApplyColorSwatch(FontColorSwatch, hex);
            _vm.OverlayFontColor = hex;
        }
    }

    private void PickBgColor_Click(object sender, RoutedEventArgs e)
    {
        using var dlg = new WinForms.ColorDialog
        {
            FullOpen = true,
            Color = HexToDrawingColor(_settings.OverlayBgColor)
        };
        if (dlg.ShowDialog() == WinForms.DialogResult.OK)
        {
            string hex = ColorToHex(dlg.Color);
            ApplyColorSwatch(BgColorSwatch, hex);
            _vm.OverlayBgColor = hex;
        }
    }

    public event Action? Saved;
    public event Action? Cancelled;

    // ── Reset ─────────────────────────────────────────────────────────────────

    private void ResetHotkeys_Click(object sender, RoutedEventArgs e)
    {
        var def = new AppSettings();
        _nextRaw = def.HotkeyNextArea;
        _prevRaw = def.HotkeyPrevArea;
        TxtNext.Text = HotkeyHelper.Display(_nextRaw);
        TxtPrev.Text = HotkeyHelper.Display(_prevRaw);
        TxtNextHint.Text = "Click to capture…";
        TxtPrevHint.Text = "Click to capture…";
    }

    private void ResetToDefaults_Click(object sender, RoutedEventArgs e)
    {
        var def = new AppSettings();
        // Apply defaults to live VM immediately
        _vm.OverlayFontSize         = def.OverlayFontSize;
        _vm.OverlayFontColor        = def.OverlayFontColor;
        _vm.OverlayFontOpacity      = def.OverlayFontOpacity;
        _vm.OverlayBgColor          = def.OverlayBgColor;
        _vm.OverlayBgOpacityPercent = def.OverlayBgOpacityPercent;
        _vm.OverlayColumns          = def.OverlayColumns;
        // Reload controls to match
        LoadOverlayControls(def);
    }

    // ── Buttons ───────────────────────────────────────────────────────────────

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        _settings.HotkeyNextArea = _nextRaw;
        _settings.HotkeyPrevArea = _prevRaw;
        if (int.TryParse(TxtPort.Text.Trim(), out int port) && port is >= 1024 and <= 65535)
            _settings.OverlayPort = port;
        Saved?.Invoke();
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        // Restore all VM properties to their original values
        _vm.OverlayFontSize         = _origFontSize;
        _vm.OverlayFontColor        = _origFontColor;
        _vm.OverlayFontOpacity      = _origFontOpacity;
        _vm.OverlayBgColor          = _origBgColor;
        _vm.OverlayBgOpacityPercent = _origBgOpacity;
        _vm.OverlayColumns          = _origColumns;
        Cancelled?.Invoke();
        Close();
    }
}
