using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace Ds1ItemTracker.Models;

public class ItemArea : INotifyPropertyChanged
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("items")]
    public ObservableCollection<ItemLocation> Items { get; set; } = new();

    // Only count verified items in progress — unverified have placeholder flag IDs
    [JsonIgnore]
    public int TotalItems => Items.Count(i => i.Verified);

    [JsonIgnore]
    public int UnverifiedCount => Items.Count(i => !i.Verified);

    [JsonIgnore]
    public int TotalAllItems => Items.Count;

    [JsonIgnore]
    public int PickedUpCount => Items.Count(i => i.Verified && i.IsPickedUp);

    [JsonIgnore]
    public string ProgressLabel => UnverifiedCount > 0
        ? $"{PickedUpCount} / {TotalItems}  ({UnverifiedCount} ⚠)"
        : $"{PickedUpCount} / {TotalItems}";

    [JsonIgnore]
    public double ProgressPercent => TotalItems == 0 ? 0 : (double)PickedUpCount / TotalItems * 100;

    public void RefreshProgress()
    {
        OnPropertyChanged(nameof(PickedUpCount));
        OnPropertyChanged(nameof(ProgressLabel));
        OnPropertyChanged(nameof(ProgressPercent));
        OnPropertyChanged(nameof(UnverifiedCount));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public class ItemDatabase
{
    [JsonPropertyName("version")]
    public string Version { get; set; } = "1.0";

    [JsonPropertyName("areas")]
    public List<ItemArea> Areas { get; set; } = new();
}
