using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace Ds1ItemTracker.Models;

public class ItemLocation : INotifyPropertyChanged
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("flagId")]
    public long FlagId { get; set; }

    private bool _verified;
    [JsonPropertyName("verified")]
    public bool Verified
    {
        get => _verified;
        set
        {
            if (_verified != value)
            {
                _verified = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DisplayState));
                OnPropertyChanged(nameof(StatusIcon));
                OnPropertyChanged(nameof(FullName));
                OnPropertyChanged(nameof(TooltipText));
            }
        }
    }

    [JsonPropertyName("notes")]
    public string Notes { get; set; } = string.Empty;

    // 0 = unverified (not tracked), 1 = tracked/not picked up, 2 = tracked/picked up
    [JsonIgnore]
    public int DisplayState => !Verified ? 0 : (_isPickedUp ? 2 : 1);

    private bool _isPickedUp;
    [JsonIgnore]
    public bool IsPickedUp
    {
        get => _isPickedUp;
        set
        {
            if (_isPickedUp != value)
            {
                _isPickedUp = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DisplayState));
                OnPropertyChanged(nameof(StatusIcon));
            }
        }
    }

    // Icon: ? = unverified/untracked, ○ = not picked up, ✓ = picked up
    [JsonIgnore]
    public string StatusIcon => !Verified ? "?" : (_isPickedUp ? "✓" : "○");

    [JsonIgnore]
    public string FullName => Name + (Verified ? "" : " ⚠");

    [JsonIgnore]
    public string TooltipText
    {
        get
        {
            var parts = new System.Collections.Generic.List<string>();
            parts.Add($"Flag ID: {FlagId}");
            if (!string.IsNullOrEmpty(Notes)) parts.Add(Notes);
            if (!Verified) parts.Add("⚠ Not tracked — use Flag Scanner to find & verify the correct Flag ID");
            return string.Join("\n", parts);
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
