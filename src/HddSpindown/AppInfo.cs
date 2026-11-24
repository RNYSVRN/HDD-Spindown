using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace HddSpindown;

/// <summary>App information for the title and about section. The version comes from &lt;Version&gt; in the csproj.</summary>
public static class AppInfo
{
    public static string Version { get; } = FormatVersion(Assembly.GetExecutingAssembly().GetName().Version);

    public static string Title => $"{Loc.AppName} {Version}";

    public static string Runtime => RuntimeInformation.FrameworkDescription;

    public static string OperatingSystem => $"{RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})";

    public static string SettingsFolder => SettingsFolderOverride ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), Services.AppSettings.FolderName);

    public static string ProgramFolder => ProgramFolderOverride ?? AppContext.BaseDirectory;

    /// <summary>Screenshot mode only: neutral sample paths instead of real user paths.</summary>
    internal static string? SettingsFolderOverride { get; set; }
    internal static string? ProgramFolderOverride { get; set; }

    // ---------- Links (empty or "#" = not available yet, the link is then shown as a placeholder) ----------
    public const string KofiUrl = "https://ko-fi.com/RNYSVRN";
    public const string KofiLabel = "ko-fi.com/RNYSVRN";
    public const string GitHubUrl = "https://github.com/RNYSVRN/HDD-Spindown";
    public const string GitHubIssuesUrl = GitHubUrl + "/issues/new";

    private static string FormatVersion(Version? v) =>
        v is null ? "?" : v.Build > 0 || v.Revision <= 0 ? $"{v.Major}.{v.Minor}.{Math.Max(v.Build, 0)}" : v.ToString();
}
