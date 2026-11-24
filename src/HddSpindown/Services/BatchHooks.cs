using System.Diagnostics;
using System.IO;
using HddSpindown.Native;

namespace HddSpindown.Services;

/// <summary>
/// Runs beforeSleep.bat / afterSleep.bat from the program folder, once per
/// volume of the drive (or once if it has no volumes).
/// Arguments: %1 drive letter (e.g. "D:", otherwise empty), %2 name, %3 device path.
/// </summary>
public static class BatchHooks
{
    public const string BeforeSleep = "beforeSleep.bat";
    public const string AfterSleep = "afterSleep.bat";
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(2);

    public static async Task RunAsync(string fileName, DiskInfo disk, IReadOnlyList<VolumeInfo> volumes, Action<string> log)
    {
        string path = Path.Combine(AppContext.BaseDirectory, fileName);
        if (!File.Exists(path)) return;

        IEnumerable<string> letters = volumes.Count > 0 ? volumes.Select(v => v.DriveLetter ?? "") : [""];
        foreach (string letter in letters)
        {
            var psi = new ProcessStartInfo("cmd.exe")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = AppContext.BaseDirectory,
            };
            psi.ArgumentList.Add("/c");
            psi.ArgumentList.Add(path);
            psi.ArgumentList.Add(letter);
            psi.ArgumentList.Add(disk.FriendlyName);
            psi.ArgumentList.Add(disk.DevicePath ?? disk.InstanceId);

            log($"{fileName} {(letter.Length > 0 ? letter : Loc.T("(ohne Buchstabe)", "(no letter)"))}");
            try
            {
                using var p = Process.Start(psi)!;
                using var cts = new CancellationTokenSource(Timeout);
                await p.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                log(Loc.T($"{fileName} läuft länger als {Timeout.TotalMinutes} min – es wird nicht weiter gewartet.", $"{fileName} is running longer than {Timeout.TotalMinutes} min – no longer waiting."));
            }
            catch (Exception ex)
            {
                log(Loc.T($"{fileName} fehlgeschlagen: {ex.Message}", $"{fileName} failed: {ex.Message}"));
            }
        }
    }
}
