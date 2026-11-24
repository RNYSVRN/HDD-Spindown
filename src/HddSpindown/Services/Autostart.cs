using System.Diagnostics;
using System.IO;
using System.Security;
using System.Security.Principal;

namespace HddSpindown.Services;

/// <summary>
/// Start with Windows via Task Scheduler. A "Run" entry in the registry
/// is not enough because the app needs admin rights; Windows would block it at sign-in.
/// The task starts the app with highest privileges when the current user signs in,
/// in the notification area only (-t), without a UAC prompt.
/// </summary>
public static class Autostart
{
    public const string TaskName = "HDD-Spindown";

    public static bool IsEnabled() => Run("/Query", "/TN", TaskName) == 0;

    public static void Enable()
    {
        string exe = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "HddSpindown.exe");
        string user = WindowsIdentity.GetCurrent().Name;
        string xml = $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo>
                <Description>HDD-Spindown beim Anmelden im Infobereich starten.</Description>
              </RegistrationInfo>
              <Triggers>
                <LogonTrigger>
                  <Enabled>true</Enabled>
                  <UserId>{SecurityElement.Escape(user)}</UserId>
                </LogonTrigger>
              </Triggers>
              <Principals>
                <Principal id="Author">
                  <UserId>{SecurityElement.Escape(user)}</UserId>
                  <LogonType>InteractiveToken</LogonType>
                  <RunLevel>HighestAvailable</RunLevel>
                </Principal>
              </Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                <Priority>7</Priority>
              </Settings>
              <Actions Context="Author">
                <Exec>
                  <Command>{SecurityElement.Escape(exe)}</Command>
                  <Arguments>-t</Arguments>
                  <WorkingDirectory>{SecurityElement.Escape(Path.GetDirectoryName(exe)!)}</WorkingDirectory>
                </Exec>
              </Actions>
            </Task>
            """;

        string file = Path.Combine(Path.GetTempPath(), $"hddspindown-task-{Guid.NewGuid():N}.xml");
        try
        {
            File.WriteAllText(file, xml, System.Text.Encoding.Unicode);
            int code = Run("/Create", "/TN", TaskName, "/XML", file, "/F");
            if (code != 0) throw new InvalidOperationException(
                Loc.T($"Aufgabe konnte nicht angelegt werden (Code {code}).", $"Could not create the scheduled task (code {code})."));
        }
        finally
        {
            try { File.Delete(file); } catch { }
        }
    }

    public static void Disable()
    {
        int code = Run("/Delete", "/TN", TaskName, "/F");
        if (code != 0 && IsEnabled()) throw new InvalidOperationException(
            Loc.T($"Aufgabe konnte nicht entfernt werden (Code {code}).", $"Could not remove the scheduled task (code {code})."));
    }

    private static int Run(params string[] args)
    {
        var psi = new ProcessStartInfo("schtasks.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        p.StandardOutput.ReadToEnd();
        p.StandardError.ReadToEnd();
        p.WaitForExit(15000);
        return p.HasExited ? p.ExitCode : -1;
    }
}
