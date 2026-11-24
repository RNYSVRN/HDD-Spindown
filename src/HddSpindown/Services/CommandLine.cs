using HddSpindown.Native;

namespace HddSpindown.Services;

public enum CliAction { None, Sleep, Lock, Wake, Show }

/// <summary>
/// Command line:
///   -s [pattern] [-m OFFLINE|DEACTIVATE]  spin down (without a pattern: all marked "spin down at startup")
///   -l  pattern  -m OFFLINE|DEACTIVATE    lock only
///   -w [pattern]                          wake up (without a pattern: all locked ones)
///   -t / --tray                           start in the notification area only
///   --lang de|en                          language (otherwise the setting or the Windows language)
/// The pattern matches the device path, instance ID, name or drive letter (e.g. "D:").
/// </summary>
public sealed record CommandLine(CliAction Action, string? Pattern, LockMode? Mode, bool Tray, string? Language = null, bool Restarted = false)
{
    public static CommandLine Parse(IReadOnlyList<string> args)
    {
        CliAction action = CliAction.None;
        string? pattern = null;
        LockMode? mode = null;
        bool tray = false;
        string? lang = null;
        bool restarted = false;

        for (int i = 0; i < args.Count; i++)
        {
            string a = args[i];
            switch (a.ToLowerInvariant())
            {
                case "-s" or "--sleep": action = CliAction.Sleep; break;
                case "-l" or "--lock": action = CliAction.Lock; break;
                case "-w" or "--wake": action = CliAction.Wake; break;
                case "-t" or "--tray": tray = true; break;
                case "--lang" when i + 1 < args.Count: lang = args[++i]; break;
                case "--restarted": restarted = true; break;
                case "--show": action = CliAction.Show; break;
                case "-m" or "--mode" when i + 1 < args.Count:
                    mode = args[++i].ToUpperInvariant() switch
                    {
                        "OFFLINE" => LockMode.Offline,
                        "DEACTIVATE" => LockMode.Deactivate,
                        "SLEEP" => LockMode.SleepOnly,
                        _ => null,
                    };
                    break;
                default:
                    if (!a.StartsWith('-')) pattern = a;
                    break;
            }
        }
        return new CommandLine(action, pattern, mode, tray, lang, restarted);
    }

    public static bool Matches(DiskInfo d, string pattern) =>
        Contains(d.DevicePath, pattern) || Contains(d.InstanceId, pattern) || Contains(d.FriendlyName, pattern)
        || d.DriveLetters.Any(l => string.Equals(l, pattern.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase));

    private static bool Contains(string? s, string p) => s?.Contains(p, StringComparison.OrdinalIgnoreCase) == true;
}
