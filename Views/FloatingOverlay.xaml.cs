using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using Ds1ItemTracker.ViewModels;

namespace Ds1ItemTracker.Views;

public partial class FloatingOverlay : Window
{
    // ── WM_NCHITTEST approach ─────────────────────────────────────────────────
    // Rather than making the whole window click-through (which makes buttons
    // unreachable), we hook WM_NCHITTEST and return HTTRANSPARENT only for the
    // item list area — the header row with the buttons stays fully interactive.
    private const int WM_NCHITTEST  = 0x0084;
    private const int HTTRANSPARENT = -1;

    private bool _clickThrough;
    private HwndSource? _hwndSource;
    private readonly MainViewModel _vm;

    public FloatingOverlay(MainViewModel vm)
    {
        _vm = vm;
        InitializeComponent();
        DataContext = vm;

        // Restore last position
        var s = vm.Settings.Current;
        Left = s.FloatLeft;
        Top  = s.FloatTop;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwndSource = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
        _hwndSource?.AddHook(WndProc);
    }

    // Save position whenever the window is moved
    protected override void OnLocationChanged(EventArgs e)
    {
        base.OnLocationChanged(e);
        _vm.Settings.Current.FloatLeft = Left;
        _vm.Settings.Current.FloatTop  = Top;
        _vm.Settings.Save();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_NCHITTEST && _clickThrough)
        {
            // Extract signed screen coords from lParam
            int screenX = unchecked((short)(lParam.ToInt64() & 0xFFFF));
            int screenY = unchecked((short)((lParam.ToInt64() >> 16) & 0xFFFF));

            // Convert screen pixels → WPF DIPs using DPI transform
            var src  = PresentationSource.FromVisual(this);
            double dpiY = src?.CompositionTarget?.TransformToDevice.M22 ?? 1.0;

            // Window-relative Y in physical pixels
            double winRelY = screenY - Top * dpiY;
            // Header height in physical pixels
            double headerPx = HeaderRow.ActualHeight * dpiY;

            if (winRelY > headerPx)
            {
                handled = true;
                return new IntPtr(HTTRANSPARENT);
            }
        }
        return IntPtr.Zero;
    }

    // Drag only when click-through is OFF
    private void Border_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!_clickThrough) DragMove();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Hide();

    private void ClickThrough_Click(object sender, RoutedEventArgs e)
    {
        _clickThrough = !_clickThrough;

        BtnClickThrough.Foreground = _clickThrough
            ? new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0xB8, 0x96, 0x0C))
            : new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0x88, 0x80, 0x70));

        BtnClickThrough.ToolTip = _clickThrough
            ? "Click-through ON — header stays interactive; click ⊕ again to disable"
            : "Toggle click-through";
    }
}

