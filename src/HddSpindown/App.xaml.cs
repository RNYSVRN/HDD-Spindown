using System.Windows;
using HddSpindown.Services;

namespace HddSpindown;

public partial class App : Application
{
    private SingleInstance? _instance;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // For the installer: create/remove the autostart task and exit right away (no window).
        // Uses the same task as the switch in the settings.
        if (e.Args.Any(a => a is "--register-autostart" or "--unregister-autostart"))
        {
            int code = 0;
            try
            {
                if (e.Args.Contains("--register-autostart")) Autostart.Enable();
                else Autostart.Disable();
            }
            catch { code = 1; }
            Shutdown(code);
            return;
        }

        _instance = new SingleInstance();
        if (!_instance.IsFirst)
        {
            // Already running: pass the command on to the main instance and exit.
            SingleInstance.Forward(e.Args);
            Shutdown();
            return;
        }

        var cmd = CommandLine.Parse(e.Args);
        Loc.Init(cmd.Language ?? AppSettings.Load().Language);
#if DEBUG
        if (Demo.Enabled) { AppSettings.ReadOnly = true; Demo.ApplyTheme(this); }
#endif
        var window = new MainWindow();
        MainWindow = window;

        // Only show the window if neither --tray nor a command was given (always after a restart).
        if (cmd.Restarted || (!cmd.Tray && cmd.Action == CliAction.None)) window.Show();

        _instance.Listen(args => Dispatcher.BeginInvoke(async () =>
            await window.ExecuteAsync(CommandLine.Parse(args))));

        await window.StartupAsync(cmd);
    }

    /// <summary>
    /// Restarts the app (e.g. after a language change). The single-instance lock is released first,
    /// otherwise the new instance would only report to the old one and exit.
    /// </summary>
    public void Restart()
    {
        _instance?.Dispose();
        _instance = null;
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!, "--restarted")
        {
            UseShellExecute = false,
            WorkingDirectory = AppContext.BaseDirectory,
        });
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _instance?.Dispose();
        base.OnExit(e);
    }
}
