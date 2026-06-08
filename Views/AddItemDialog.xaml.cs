using System.Windows;
using System.Windows.Controls;
using Ds1ItemTracker.Models;

namespace Ds1ItemTracker.Views;

public partial class AddItemDialog : Window
{
    private const string NEW_AREA_SENTINEL = "— New area… —";

    // ── Input ────────────────────────────────────────────────────────────────
    public int FlagId { get; }
    public IReadOnlyList<ItemArea> ExistingAreas { get; }

    // ── Result (set on confirm) ───────────────────────────────────────────────
    public string ItemName  { get; private set; } = string.Empty;
    public string AreaId    { get; private set; } = string.Empty;
    public string AreaName  { get; private set; } = string.Empty;
    public string Notes     { get; private set; } = string.Empty;
    public bool   IsNewArea { get; private set; }

    public AddItemDialog(int flagId, IReadOnlyList<ItemArea> existingAreas)
    {
        FlagId        = flagId;
        ExistingAreas = existingAreas;

        InitializeComponent();

        TxtFlagId.Text = flagId.ToString();

        // Populate area combo
        foreach (var area in existingAreas)
            CmbArea.Items.Add(area.Name);
        CmbArea.Items.Add(NEW_AREA_SENTINEL);

        if (CmbArea.Items.Count > 0)
            CmbArea.SelectedIndex = 0;
    }

    // ── Validation ────────────────────────────────────────────────────────────
    private void Validate(object sender, TextChangedEventArgs e) => ValidateForm();

    private void ValidateForm()
    {
        bool nameOk = !string.IsNullOrWhiteSpace(TxtItemName.Text);
        bool areaOk = CmbArea.SelectedItem is string sel && sel != NEW_AREA_SENTINEL
                   || (PanelNewArea.Visibility == Visibility.Visible
                       && !string.IsNullOrWhiteSpace(TxtNewArea.Text));

        if (BtnAdd != null) BtnAdd.IsEnabled = nameOk && areaOk;
    }

    private void CmbArea_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        bool isNew = CmbArea.SelectedItem is string s && s == NEW_AREA_SENTINEL;
        PanelNewArea.Visibility = isNew ? Visibility.Visible : Visibility.Collapsed;
        ValidateForm();
    }

    // ── Buttons ───────────────────────────────────────────────────────────────
    private void Add_Click(object sender, RoutedEventArgs e)
    {
        ItemName = TxtItemName.Text.Trim();
        Notes    = TxtNotes.Text.Trim();

        bool isNew = CmbArea.SelectedItem is string s && s == NEW_AREA_SENTINEL;
        IsNewArea = isNew;

        if (isNew)
        {
            AreaName = TxtNewArea.Text.Trim();
            AreaId   = Slugify(AreaName);
        }
        else
        {
            AreaName = (string)CmbArea.SelectedItem!;
            AreaId   = ExistingAreas.First(a => a.Name == AreaName).Id;
        }

        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    // ── Helpers ───────────────────────────────────────────────────────────────
    private static string Slugify(string text)
        => System.Text.RegularExpressions.Regex
               .Replace(text.ToLowerInvariant().Trim(), @"[^a-z0-9]+", "_")
               .Trim('_');
}
