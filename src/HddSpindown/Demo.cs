#if DEBUG
using System.Windows;
using System.Windows.Media;

namespace HddSpindown;

/// <summary>
/// Debug build only: screenshot mode for README/release images.
///   HDDSPINDOWN_DEMO=1            sample states + labels, nothing is saved, no admin bar
///   HDDSPINDOWN_THEME=light|dark  force the app theme (Windows stays unchanged)
///   HDDSPINDOWN_ACCENT=#RRGGBB    override the accent color in the app only
/// </summary>
internal static class Demo
{
    public static bool Enabled => Environment.GetEnvironmentVariable("HDDSPINDOWN_DEMO") == "1";

    public static void ApplyTheme(Application app)
    {
        AppInfo.SettingsFolderOverride = @"C:\Users\User\AppData\Roaming\HDD-Spindown";
        AppInfo.ProgramFolderOverride = @"C:\Program Files\HDD-Spindown\";

        switch (Environment.GetEnvironmentVariable("HDDSPINDOWN_THEME"))
        {
            case "light": app.ThemeMode = ThemeMode.Light; break;
            case "dark": app.ThemeMode = ThemeMode.Dark; break;
        }

        if (Environment.GetEnvironmentVariable("HDDSPINDOWN_ACCENT") is { Length: > 0 } hex &&
            ColorConverter.ConvertFromString(hex) is Color accent)
        {
            // The same shades Windows derives from the accent color (lighter/darker)
            // The Fluent theme reads the accent color via the SystemColors keys (color and brush)
            void Set(ResourceKey colorKey, ResourceKey brushKey, Color c)
            {
                app.Resources[colorKey] = c;
                app.Resources[brushKey] = new SolidColorBrush(c);
            }
            Set(SystemColors.AccentColorKey, SystemColors.AccentColorBrushKey, accent);
            Set(SystemColors.AccentColorLight1Key, SystemColors.AccentColorLight1BrushKey, Mix(accent, Colors.White, 0.25));
            Set(SystemColors.AccentColorLight2Key, SystemColors.AccentColorLight2BrushKey, Mix(accent, Colors.White, 0.45));
            Set(SystemColors.AccentColorLight3Key, SystemColors.AccentColorLight3BrushKey, Mix(accent, Colors.White, 0.65));
            Set(SystemColors.AccentColorDark1Key, SystemColors.AccentColorDark1BrushKey, Mix(accent, Colors.Black, 0.2));
            Set(SystemColors.AccentColorDark2Key, SystemColors.AccentColorDark2BrushKey, Mix(accent, Colors.Black, 0.4));
            Set(SystemColors.AccentColorDark3Key, SystemColors.AccentColorDark3BrushKey, Mix(accent, Colors.Black, 0.6));
        }
    }

    private static Color Mix(Color a, Color b, double t) => Color.FromRgb(
        (byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));
}
#endif
