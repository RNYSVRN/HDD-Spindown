using System.ComponentModel;
using System.Windows.Media;

namespace HddSpindown;

/// <summary>A color of the label palette.</summary>
public sealed class TagColor
{
    public required string Key { get; init; }
    public required string Label { get; init; }
    public required Color Color { get; init; }
    public SolidColorBrush Brush => TagPalette.BrushFor(Key);
}

/// <summary>
/// Label colors, based on the Windows 11 accent colors. The color belongs to the label,
/// not the drive: "Movies" has the same color on every drive.
/// </summary>
public static class TagPalette
{
    public const string AccentKey = "accent";

    public static IReadOnlyList<TagColor> All { get; } =
    [
        new() { Key = AccentKey, Label = Loc.T("Akzentfarbe", "Accent color"), Color = AccentColor() },
        new() { Key = "red",    Label = Loc.T("Rot", "Red"),          Color = Color.FromRgb(0xE8, 0x48, 0x48) },
        new() { Key = "orange", Label = Loc.T("Orange", "Orange"),    Color = Color.FromRgb(0xF7, 0x8C, 0x2B) },
        new() { Key = "yellow", Label = Loc.T("Gelb", "Yellow"),      Color = Color.FromRgb(0xF2, 0xC8, 0x11) },
        new() { Key = "green",  Label = Loc.T("Grün", "Green"),       Color = Color.FromRgb(0x4C, 0xB8, 0x4C) },
        new() { Key = "teal",   Label = Loc.T("Türkis", "Teal"),      Color = Color.FromRgb(0x14, 0xB8, 0xA6) },
        new() { Key = "blue",   Label = Loc.T("Blau", "Blue"),        Color = Color.FromRgb(0x3B, 0x8E, 0xEA) },
        new() { Key = "purple", Label = Loc.T("Lila", "Purple"),      Color = Color.FromRgb(0x9B, 0x6B, 0xE8) },
        new() { Key = "pink",   Label = Loc.T("Pink", "Pink"),        Color = Color.FromRgb(0xE3, 0x5D, 0xA8) },
        new() { Key = "gray",   Label = Loc.T("Grau", "Gray"),        Color = Color.FromRgb(0x8E, 0x8E, 0x93) },
    ];

    private static readonly Dictionary<string, SolidColorBrush> Brushes = new();

    /// <summary>Label name -> color key (from settings.json, shared by all drives).</summary>
    private static Dictionary<string, string> _colors = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>A label color was changed (save settings, update pills).</summary>
    public static event Action? ColorsChanged;

    public static void Load(Dictionary<string, string> colors) => _colors = colors;

    public static string ColorOf(string tag) =>
        _colors.TryGetValue(tag, out var key) && All.Any(c => c.Key == key) ? key : AccentKey;

    public static void SetColor(string tag, string key)
    {
        if (ColorOf(tag) == key && _colors.ContainsKey(tag)) return;
        _colors[tag] = key;
        ColorsChanged?.Invoke();
    }

    public static SolidColorBrush BrushFor(string key)
    {
        if (!Brushes.TryGetValue(key, out var brush))
        {
            var color = All.FirstOrDefault(c => c.Key == key)?.Color ?? AccentColor();
            brush = new SolidColorBrush(color);
            brush.Freeze();
            Brushes[key] = brush;
        }
        return brush;
    }

    /// <summary>
    /// Accent color as the app shows it: the lighter variant in dark mode (like AccentFillColorDefault),
    /// the darker one in light mode.
    /// </summary>
    private static Color AccentColor()
    {
        try
        {
            bool light = Microsoft.Win32.Registry.GetValue(
                @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1) is int v && v != 0;
            return light ? System.Windows.SystemColors.AccentColorDark1 : System.Windows.SystemColors.AccentColorLight2;
        }
        catch { return Color.FromRgb(0x00, 0x67, 0xC0); }
    }
}

/// <summary>A label of a drive, with its (shared) color.</summary>
public sealed class TagItem : INotifyPropertyChanged
{
    public TagItem(string name) => Name = name;

    public string Name { get; }
    public string ColorKey => TagPalette.ColorOf(Name);
    public SolidColorBrush Brush => TagPalette.BrushFor(ColorKey);

    public void Refresh() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));

    public event PropertyChangedEventHandler? PropertyChanged;
    public override string ToString() => Name;
}
