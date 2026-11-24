using System.IO;
using System.Text.Json;

namespace HddSpindown.Services;

public enum LockMode
{
    SleepOnly,   // lock volumes + spin down
    Offline,     // + drive offline
    Deactivate,  // + disable device
}

public sealed class DiskSettings
{
    public LockMode Mode { get; set; } = LockMode.Offline;
    /// <summary>Spin down automatically when the app starts.</summary>
    public bool AutoSleep { get; set; }

    /// <summary>Custom labels, e.g. "Movies" or "Backup".</summary>
    public List<string> Tags { get; set; } = new();
}

public sealed class AppSettings
{
    /// <summary>Settings per device instance ID.</summary>
    public Dictionary<string, DiskSettings> Disks { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public DiskSettings For(string instanceId)
    {
        if (!Disks.TryGetValue(instanceId, out var s)) Disks[instanceId] = s = new DiskSettings();
        return s;
    }

    /// <summary>Show the device path as a tooltip over the drive name.</summary>
    public bool ShowDiskTooltips { get; set; } = true;

    /// <summary>Notification in the notification area after spin down/wake up.</summary>
    public bool Notifications { get; set; } = true;

    /// <summary>Color per label (name -> color key), shared by all drives.</summary>
    public Dictionary<string, string> TagColors { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The notice for the "disable" mode has already been confirmed.</summary>
    public bool DeactivateHintShown { get; set; }

    /// <summary>"auto" (system language), "de" or "en".</summary>
    public string Language { get; set; } = "auto";

    private static string AppData => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    public const string FolderName = "HDD-Spindown";
    public static string Folder => Path.Combine(AppData, FolderName);
    private static string FilePath => Path.Combine(Folder, "settings.json");
    // Settings from earlier versions (folder "HddSpindown") are migrated once.
    private static readonly string[] LegacyFilePaths =
    [
        Path.Combine(AppData, "HddSpindown", "settings.json"),
    ];

    public static AppSettings Load()
    {
        try
        {
            string? file = File.Exists(FilePath) ? FilePath : LegacyFilePaths.FirstOrDefault(File.Exists);
            if (file is not null)
            {
                var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(file));
                if (s is not null)
                {
                    s.Disks = new Dictionary<string, DiskSettings>(s.Disks, StringComparer.OrdinalIgnoreCase);
                    s.TagColors = new Dictionary<string, string>(s.TagColors ?? new(), StringComparer.OrdinalIgnoreCase);
                    // Read from the old folder: copy to the new "HDD-Spindown" folder right away
                    if (!string.Equals(file, FilePath, StringComparison.OrdinalIgnoreCase)) s.Save();
                    return s;
                }
            }
        }
        catch { /* corrupt file -> defaults */ }
        return new();
    }

    /// <summary>Don't save anything (screenshot mode in the debug build).</summary>
    public static bool ReadOnly { get; set; }

    public void Save()
    {
        if (ReadOnly) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* saving is not critical */ }
    }
}
