using System.IO;

namespace HddSpindown.Services;

/// <summary>
/// Writes the log to %APPDATA%\HDD-Spindown\log.txt as well, so you can check afterwards
/// when something happened (e.g. overnight). Rotates at 1 MB (log.old.txt keeps the previous part).
/// </summary>
public static class AppLog
{
    private const long MaxBytes = 1_000_000;
    private static readonly object Gate = new();

    public static string FilePath => Path.Combine(AppSettings.Folder, "log.txt");

    public static void Write(string line)
    {
        if (AppSettings.ReadOnly) return;
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(AppSettings.Folder);
                var file = new FileInfo(FilePath);
                if (file.Exists && file.Length > MaxBytes)
                    File.Move(FilePath, Path.Combine(AppSettings.Folder, "log.old.txt"), overwrite: true);
                File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {line}{Environment.NewLine}");
            }
        }
        catch { /* logging must never break the app */ }
    }
}
