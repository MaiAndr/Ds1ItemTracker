using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Ds1ItemTracker.Helpers;
using Ds1ItemTracker.Services;

namespace Ds1ItemTracker.Views;

public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;

    // Raw key strings built while the user types
    private string _nextRaw;
    private string _prevRaw;

    public SettingsWindow(AppSettings settings)
    {
        InitializeComponent();
        _settings = settings;

        _nextRaw = settings.HotkeyNextArea;
        _prevRaw = settings.HotkeyPrevArea;

        TxtNext.Text = HotkeyHelper.Display(_nextRaw);
        TxtPrev.Text = HotkeyHelper.Display(_prevRaw);
        TxtPort.Text = settings.OverlayPort.ToString();
    }

    // ── Key capture ───────────────────────────────────────────────────────────

    private void HotkeyBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;

        // Ignore bare modifier presses — wait for the real key
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift
                or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin)
            return;

        // Escape = clear
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

    // ── Buttons ───────────────────────────────────────────────────────────────

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        _settings.HotkeyNextArea = _nextRaw;
        _settings.HotkeyPrevArea = _prevRaw;
        if (int.TryParse(TxtPort.Text.Trim(), out int port) && port is >= 1024 and <= 65535)
            _settings.OverlayPort = port;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
        => DialogResult = false;
}
