using System.Windows.Input;

namespace Ds1ItemTracker.Helpers;

/// <summary>
/// Converts between a hotkey string like "Ctrl+Right" and
/// the WPF Key / ModifierKeys pair used for InputBindings.
/// </summary>
public static class HotkeyHelper
{
    /// <summary>Parse "Ctrl+Shift+F5" → (Key.F5, Ctrl|Shift). Returns null on failure.</summary>
    public static (Key Key, ModifierKeys Mods)? Parse(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;

        var parts = s.Split('+');
        ModifierKeys mods = ModifierKeys.None;
        Key key = Key.None;

        foreach (var part in parts)
        {
            switch (part.Trim().ToLowerInvariant())
            {
                case "ctrl":  mods |= ModifierKeys.Control; break;
                case "shift": mods |= ModifierKeys.Shift;   break;
                case "alt":   mods |= ModifierKeys.Alt;     break;
                case "win":   mods |= ModifierKeys.Windows; break;
                default:
                    if (!Enum.TryParse<Key>(part.Trim(), ignoreCase: true, out key))
                        return null;
                    break;
            }
        }

        return key == Key.None ? null : (key, mods);
    }

    /// <summary>Format (Key.Right, Ctrl) → "Ctrl+Right".</summary>
    public static string Format(Key key, ModifierKeys mods)
    {
        var parts = new List<string>();
        if ((mods & ModifierKeys.Control) != 0) parts.Add("Ctrl");
        if ((mods & ModifierKeys.Shift)   != 0) parts.Add("Shift");
        if ((mods & ModifierKeys.Alt)     != 0) parts.Add("Alt");
        if ((mods & ModifierKeys.Windows) != 0) parts.Add("Win");
        parts.Add(key.ToString());
        return string.Join("+", parts);
    }

    /// <summary>Produce a human-readable display string, e.g. "Ctrl + →".</summary>
    public static string Display(string? s)
    {
        var parsed = Parse(s);
        if (parsed == null) return s ?? "(none)";
        var (key, mods) = parsed.Value;

        var parts = new List<string>();
        if ((mods & ModifierKeys.Control) != 0) parts.Add("Ctrl");
        if ((mods & ModifierKeys.Shift)   != 0) parts.Add("Shift");
        if ((mods & ModifierKeys.Alt)     != 0) parts.Add("Alt");
        if ((mods & ModifierKeys.Windows) != 0) parts.Add("Win");
        parts.Add(KeyDisplayName(key));
        return string.Join(" + ", parts);
    }

    private static string KeyDisplayName(Key k) => k switch
    {
        Key.Left     => "←",
        Key.Right    => "→",
        Key.Up       => "↑",
        Key.Down     => "↓",
        Key.PageUp   => "PgUp",
        Key.PageDown => "PgDn",
        Key.OemComma => ",",
        Key.OemPeriod => ".",
        _            => k.ToString()
    };
}
