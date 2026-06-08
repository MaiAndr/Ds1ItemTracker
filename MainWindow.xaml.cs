using System.Windows;
using Ds1ItemTracker.ViewModels;
using Ds1ItemTracker.Views;

namespace Ds1ItemTracker;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private FloatingOverlay? _floatingOverlay;

    public MainWindow()
    {
        InitializeComponent();
        _vm = new MainViewModel();
        DataContext = _vm;
    }

    protected override void OnClosed(EventArgs e)
    {
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

    private void OverlayUrl_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
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
}
