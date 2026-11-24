using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Security.Principal;
using System.Windows;
using System.Windows.Threading;
using HddSpindown.Native;
using HddSpindown.Services;

namespace HddSpindown;

public partial class MainWindow : Window
{
    private readonly AppSettings _settings = AppSettings.Load();
    private readonly DiskController _controller = new();
    private readonly ObservableCollection<DiskItem> _disks = new();
    private readonly DispatcherTimer _powerTimer = new() { Interval = TimeSpan.FromSeconds(15) };
    private readonly TrayIcon _tray = new();
    private bool _exiting;
    private bool _hintShown;

    public MainWindow()
    {
        TagPalette.Load(_settings.TagColors);
        TagPalette.ColorsChanged += () =>
        {
            _settings.Save();
            foreach (var d in _disks) d.RefreshTagColors();
        };
        InitializeComponent();
        DiskList.ItemsSource = _disks;

        _controller.Log += line => Dispatcher.BeginInvoke(() => Log(line));
        _controller.StateChanged += () => Dispatcher.BeginInvoke(SyncLockState);

        _tray.OpenRequested += ShowFromTray;
        _tray.ExitRequested += () => _ = ExitAsync();
        _tray.WakeAllRequested += () => _ = WakeAllAsync();
        _tray.SleepSelectedRequested += () => _ = SleepAutoAsync();
        _tray.SleepRequested += d => _ = SleepAsync(d, sleep: true);
        _tray.WakeRequested += d => _ = WakeAsync(d);

        _powerTimer.Tick += async (_, _) => await PollPowerAsync();
        SettingsPanel.BackRequested += () => ShowSettings(false);
        DiskItem.ConfirmDeactivate = ConfirmDeactivate;
        SettingsPanel.Initialize(_settings);
        SettingsPanel.RestartRequested += () => _ = ExitAsync(restart: true);
        SettingsPanel.TooltipsChanged += on => { foreach (var d in _disks) d.ShowTooltip = on; };
        SizeChanged += (_, _) => UpdateLayoutMode();

        DiskItem.CanControl = IsElevated();
        if (!DiskItem.CanControl)
            ShowInfo("SystemFillColorCautionBackgroundBrush", "", Loc.T("Ohne Adminrechte gestartet", "Started without administrator rights"),
                Loc.T("Datenträger können nur angezeigt, aber nicht gesteuert werden. Starte die App als Administrator.", "Drives can only be displayed, not controlled. Run the app as administrator."));

        LoadDisks();
        _powerTimer.Start();
        _ = PollPowerAsync();

#if DEBUG
        // Debug build only: HDDSPINDOWN_DEMO_INUSE=E: shows the "still in use" dialog with a real search (for testing without admin rights)
        if (Environment.GetEnvironmentVariable("HDDSPINDOWN_DEMO_INUSE") is { Length: > 0 } demo)
            Loaded += (_, _) => Dispatcher.BeginInvoke(() =>
            {
                if (_disks.FirstOrDefault(x => x.Info.DriveLetters.Contains(demo, StringComparer.OrdinalIgnoreCase)) is not { } dd) return;
                var procs = OpenFileFinder.Find(dd.Info.Volumes, TimeSpan.FromSeconds(8));
                ShowInUseDialog(dd, new DiskInUseException("demo", procs, new Exception()));
            }, DispatcherPriority.ApplicationIdle);

        if (Demo.Enabled) ApplyDemoStates();
#endif
    }

#if DEBUG
    /// <summary>Screenshot mode: show typical states and labels (not saved).</summary>
    private void ApplyDemoStates()
    {
        _powerTimer.Stop();
        DiskItem.CanControl = true;
        InfoBar.Visibility = Visibility.Collapsed;

        DiskItem? By(string letter) => _disks.FirstOrDefault(d => d.Info.DriveLetters.Contains(letter, StringComparer.OrdinalIgnoreCase));
        // Replace existing (real) labels with samples in the display language, in memory only
        foreach (var d in _disks) d.Tags.Clear();
        void Tag(DiskItem? d, string de, string en, string color) { if (d is not null) d.AddTag(Loc.T(de, en), color); }

        var now = DateTime.Now;
        if (By("E:") is { } e) { e.SetMode(LockMode.Offline); e.AutoSleep = true; e.Power = PowerState.Active; Tag(e, "Backup", "Backup", "green"); }
        if (By("F:") is { } f)
        {
            f.SetMode(LockMode.Offline); f.Info = f.Info with { IsOffline = true }; f.IsLocked = true;
            f.MarkAsleep(now.AddMinutes(-35)); f.Power = PowerState.Standby;
            Tag(f, "Dokumente", "Documents", "blue");
        }
        if (By("G:") is { } g) { g.SetMode(LockMode.SleepOnly); g.MarkAsleep(now.AddMinutes(-12)); g.Power = PowerState.Standby; Tag(g, "Archiv", "Archive", "orange"); }
        if (By("H:") is { } h) { h.SetMode(LockMode.Offline); h.MarkAwake(now.AddMinutes(-48)); h.Power = PowerState.Active; }
        if (By("I:") is { } i) { i.SetMode(LockMode.Offline); i.AutoSleep = true; i.Power = PowerState.Active; Tag(i, "Filme", "Movies", "purple"); }
        if (By("J:") is { } j) { j.SetMode(LockMode.Offline); j.IsLocked = true; j.MarkAsleep(now.AddHours(-2)); j.Power = PowerState.Standby; Tag(j, "Serien", "TV shows", "pink"); }
        RebuildTray();
    }
#endif

    // ---------- Startup / command line ----------

    /// <summary>Called on first start: runs commands or spins down drives marked "spin down at startup".</summary>
    public async Task StartupAsync(CommandLine cmd)
    {
        if (cmd.Restarted) return;   // don't spin anything down automatically after a restart for a language change
        if (cmd.Action is CliAction.Sleep or CliAction.Lock && cmd.Pattern is not null)
            await ExecuteAsync(cmd);
        else if (cmd.Action != CliAction.Wake)
            await SleepAutoAsync();   // Spin down at startup, except with -w
    }

    public async Task ExecuteAsync(CommandLine cmd)
    {
        switch (cmd.Action)
        {
            case CliAction.Show:
                ShowFromTray();
                break;
            case CliAction.Sleep or CliAction.Lock when cmd.Pattern is null:
                await SleepAutoAsync();
                break;
            case CliAction.Sleep or CliAction.Lock:
                if (Find(cmd.Pattern!) is { } d)
                {
                    if (cmd.Mode is { } m) d.SetMode(m);
                    if (cmd.Action == CliAction.Lock && d.Mode == LockMode.SleepOnly)
                    {
                        Log(Loc.T("-l braucht -m OFFLINE oder -m DEACTIVATE.", "-l requires -m OFFLINE or -m DEACTIVATE."));
                        break;
                    }
                    await SleepAsync(d, sleep: cmd.Action == CliAction.Sleep);
                }
                break;
            case CliAction.Wake when cmd.Pattern is null:
                await WakeAllAsync();
                break;
            case CliAction.Wake:
                if (Find(cmd.Pattern!) is { } w) await WakeAsync(w);
                break;
        }
    }

    private DiskItem? Find(string pattern)
    {
        var d = _disks.FirstOrDefault(x => CommandLine.Matches(x.Info, pattern));
        if (d is null) Log(Loc.T($"Kein Datenträger passt zu \"{pattern}\".", $"No drive matches \"{pattern}\"."));
        else if (d.IsReadOnly) { Log(Loc.T($"{d.Title}: {d.ProtectedReason} Befehl ignoriert.", $"{d.Title}: {d.ProtectedReason} Command ignored.")); return null; }
        return d;
    }

    // ---------- Drives ----------

    private void LoadDisks()
    {
        List<DiskInfo> found;
        try { found = DiskEnumerator.GetDisks(); }
        catch (Exception ex) { Log(Loc.T("Fehler beim Einlesen der Datenträger: ", "Error while scanning drives: ") + ex.Message); return; }
        ApplyDisks(found);
    }

    private bool _refreshing;

    /// <summary>"Refresh": re-read the drives in the background while showing the loading ring above the list.</summary>
    private async Task RefreshAsync()
    {
        if (_refreshing) return;
        _refreshing = true;
        RefreshButton.IsEnabled = false;
        RefreshOverlay.Visibility = Visibility.Visible;
        var started = DateTime.UtcNow;
        try
        {
            var found = await Task.Run(DiskEnumerator.GetDisks);
            ApplyDisks(found);
            await PollPowerAsync();
            Log(Loc.T($"{_disks.Count} Datenträger eingelesen.", $"{_disks.Count} drives scanned."));
        }
        catch (Exception ex) { Log(Loc.T("Fehler beim Einlesen der Datenträger: ", "Error while scanning drives: ") + ex.Message); }
        finally
        {
            // Keep it visible briefly so the feedback is noticeable even when reading is fast
            var rest = TimeSpan.FromMilliseconds(600) - (DateTime.UtcNow - started);
            if (rest > TimeSpan.Zero) await Task.Delay(rest);
            RefreshOverlay.Visibility = Visibility.Collapsed;
            RefreshButton.IsEnabled = true;
            _refreshing = false;
        }
    }

    private void ApplyDisks(List<DiskInfo> found)
    {

        foreach (var info in found)
        {
            var existing = _disks.FirstOrDefault(d => d.InstanceId == info.InstanceId);
            if (existing is not null)
            {
                // Locked/offline/disabled drives no longer have visible volumes for Windows.
                // Keep the last known ones then, so the card and tray still show "J:" instead of "no letter".
                bool hidden = existing.IsLocked || info.IsOffline || info.IsDisabled;
                existing.Info = hidden && info.Volumes.Count == 0 && existing.Info.Volumes.Count > 0
                    ? info with { Volumes = existing.Info.Volumes, DiskNumber = info.DiskNumber >= 0 ? info.DiskNumber : existing.Info.DiskNumber }
                    : info;
                continue;
            }

            var item = new DiskItem(info, _settings.For(info.InstanceId)) { ShowTooltip = _settings.ShowDiskTooltips };
            item.SettingsChanged += () => { _settings.Save(); RebuildTray(); };
            _disks.Add(item);
        }
        // Remove vanished drives, unless we locked them.
        foreach (var gone in _disks.Where(d => found.All(f => f.InstanceId != d.InstanceId) && !d.IsLocked).ToList())
            _disks.Remove(gone);

        SyncLockState();
        EmptyText.Visibility = _disks.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SyncLockState()
    {
        foreach (var d in _disks) d.IsLocked = _controller.IsLocked(d.InstanceId);
        RebuildTray();
    }

    private void RebuildTray()
    {
        // "Wake all" only if something is asleep/locked; "Spin down selected" only if there is something to do.
        bool canWakeAll = !_bulkRunning && _disks.Any(d => d.CanWake);
        bool canSleepSelected = !_bulkRunning && _disks.Any(d => d.WantsAutoSleep);
        WakeAllButton.IsEnabled = canWakeAll;
        SleepSelectedButton.IsEnabled = canSleepSelected;

        _tray.Rebuild(_disks, canWakeAll, canSleepSelected);
        // Icon: moon as soon as something is asleep. Exact count in the tooltip.
        int asleep = _disks.Count(d => d.IsAsleep);
        int locked = _disks.Count(d => d.IsLocked && !d.IsAsleep);
        _tray.SetSleepingCount(asleep);
        var parts = new List<string>();
        if (asleep > 0) parts.Add(asleep == 1 ? Loc.T("1 Platte schläft", "1 drive asleep") : Loc.T($"{asleep} Platten schlafen", $"{asleep} drives asleep"));
        if (locked > 0) parts.Add(locked == 1 ? Loc.T("1 gesperrt", "1 locked") : Loc.T($"{locked} gesperrt", $"{locked} locked"));
        _tray.SetText(parts.Count == 0 ? Loc.T($"{Loc.AppName}\nAlle Platten wach", $"{Loc.AppName}\nAll drives awake")
                                       : $"{Loc.AppName}\n{string.Join(", ", parts)}");
    }

    private bool _bulkRunning;

    private async Task PollPowerAsync()
    {
#if DEBUG
        if (Demo.Enabled) { RebuildTray(); return; }   // don't overwrite the sample states
#endif
        foreach (var d in _disks.ToList())
        {
            var info = d.Info;
            if (info.Bus != BusKind.Ata || info.DevicePath is null || info.IsDisabled) { d.Power = PowerState.Unknown; continue; }
            d.Power = await Task.Run(() => DiskControl.GetPowerState(info.DevicePath, info.Bus));
        }
        RebuildTray();
    }

    // ---------- Actions ----------

    private Task SleepAsync(DiskItem d, bool sleep) => Run(d,
        sleep ? Loc.T("Wird schlafen gelegt …", "Spinning down …") : Loc.T("Wird gesperrt …", "Locking …"),
        () => _controller.SleepAsync(d.Info, d.Mode, sleep),
        onSuccess: () => { if (sleep) d.MarkAsleep(DateTime.Now); },
        notifyText: sleep ? Loc.T("Schlafen gelegt", "Spun down") : Loc.T("Gesperrt", "Locked"));

    private Task WakeAsync(DiskItem d) => Run(d,
        Loc.T("Wird aufgeweckt …", "Waking up …"),
        () => _controller.WakeAsync(d.Info),
        onSuccess: () => d.MarkAwake(DateTime.Now),
        notifyText: Loc.T("Aufgeweckt", "Woken up"));

    private async Task SleepAutoAsync()
    {
        var targets = _disks.Where(d => d.WantsAutoSleep).ToList();
        if (targets.Count == 0) return;
        Log(Loc.T($"Lege {targets.Count} ausgewählte Festplatte(n) schlafen …", $"Spinning down {targets.Count} selected drive(s) …"));
        await Bulk(targets, d => SleepAsync(d, sleep: true));
    }

    /// <summary>Wakes everything that is asleep or locked, including drives Windows spun down by itself.</summary>
    private Task WakeAllAsync() => Bulk(_disks.Where(d => d.CanWake).ToList(), WakeAsync);

    private async Task Bulk(List<DiskItem> targets, Func<DiskItem, Task> action)
    {
        if (_bulkRunning || targets.Count == 0) return;
        _bulkRunning = true;
        RebuildTray();
        try { foreach (var d in targets) await action(d); }
        finally
        {
            _bulkRunning = false;
            RebuildTray();
        }
    }

    /// <summary>
    /// Runs an action. Success shows up as a timestamp in the card's detail line
    /// (no extra line); the status line only shows "running …" and errors.
    /// </summary>
    private async Task Run(DiskItem d, string busyText, Func<Task> action, Action onSuccess, string notifyText)
    {
        if (d.IsBusy || d.IsReadOnly) return;
        d.IsBusy = true;
        d.BusyText = busyText;
        RebuildTray();
        d.Status = "";   // the bar shows progress; the status line is kept for errors
        DiskInUseException? inUse = null;
        try
        {
            await action();
            d.Status = "";
            onSuccess();
            if (_settings.Notifications) _tray.Notify(d.Title, notifyText);
        }
        catch (DiskInUseException ex)
        {
            inUse = ex;
            d.Status = ex.Message;
            Log($"{d.Title}: {ex.Message}");
            if (!IsVisible) _tray.Notify(d.Title, ex.Message);
        }
        catch (Exception ex)
        {
            d.Status = Loc.T("Fehler: ", "Error: ") + ex.Message;
            Log($"{d.Title}: {ex.Message}");
            // Always report errors when the window is not visible
            if (!IsVisible) _tray.Notify(d.Title, ex.Message);
        }
        finally
        {
            d.IsBusy = false;
            LoadDisks();
            await PollPowerAsync();
        }

        // Drive in use: show which programs hold it and offer a retry
        if (inUse is not null && IsVisible && ShowInUseDialog(d, inUse))
            await Run(d, busyText, action, onSuccess, notifyText);
    }

    /// <summary>"Drive is still in use" dialog with a list of programs. true = retry.</summary>
    private bool ShowInUseDialog(DiskItem d, DiskInUseException ex)
    {
        string list = ex.Processes.Count > 0
            ? string.Join("\n", ex.Processes.Select(p => "•  " + p))
            : Loc.T("•  Programm nicht ermittelbar (z. B. ein Dienst, die Windows-Suche oder ein Virenscanner)",
                    "•  Program could not be identified (e.g. a service, Windows Search or a virus scanner)");
        var answer = FluentDialog.Show(this,
            Loc.T("Platte wird noch verwendet", "Drive is still in use"),
            Loc.T($"„{d.Title}“ lässt sich nicht sperren, weil diese Programme noch Dateien oder Ordner darauf geöffnet haben:\n\n{list}",
                  $"“{d.Title}” can't be locked because these programs still have files or folders open on it:\n\n{list}"),
            primary: Loc.T("Erneut versuchen", "Try again"),
            close: Loc.T("Schließen", "Close"),
            note: Loc.T("Schließe die Fenster oder Dateien (oder beende das Programm) und versuche es erneut. HDD-Spindown erzwingt nichts, damit keine ungespeicherten Daten verloren gehen.",
                        "Close the windows or files (or exit the program) and try again. HDD-Spindown doesn't force anything, so no unsaved data is lost."),
            glyph: "");
        return answer == DialogResultKind.Primary;
    }

    private void Sleep_Click(object sender, RoutedEventArgs e) { if (ItemOf(sender) is { } d) _ = SleepAsync(d, sleep: true); }
    private void Lock_Click(object sender, RoutedEventArgs e) { if (ItemOf(sender) is { } d) _ = SleepAsync(d, sleep: false); }
    private void Wake_Click(object sender, RoutedEventArgs e) { if (ItemOf(sender) is { } d) _ = WakeAsync(d); }
    private void WakeAll_Click(object sender, RoutedEventArgs e) => _ = WakeAllAsync();
    private void SleepAuto_Click(object sender, RoutedEventArgs e) => _ = SleepAutoAsync();
    private void Refresh_Click(object sender, RoutedEventArgs e) => _ = RefreshAsync();

    // ---------- Labels ----------

    private static DiskItem? DiskOf(object sender)
    {
        for (DependencyObject? d = sender as DependencyObject; d is not null;
             d = System.Windows.Media.VisualTreeHelper.GetParent(d) ?? LogicalTreeHelper.GetParent(d))
            if (d is FrameworkElement { DataContext: DiskItem item }) return item;
        return null;
    }

    private void RemoveTag_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: TagItem tag } && DiskOf(sender) is { } d) d.RemoveTag(tag);
    }

    /// <summary>Color chosen in the add menu (list of swatches in the same popup).</summary>
    private static string? SelectedNewTagColor(DependencyObject inside)
    {
        for (DependencyObject? p = inside; p is not null; p = LogicalTreeHelper.GetParent(p) ?? System.Windows.Media.VisualTreeHelper.GetParent(p))
            if (p is System.Windows.Controls.Primitives.Popup { Child: DependencyObject root })
                return FindLogical<System.Windows.Controls.ListBox>(root)?.SelectedValue as string;
        return null;
    }

    private void AddTag_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: System.Windows.Controls.TextBox box } && DiskOf(sender) is { } d &&
            d.AddTag(box.Text, SelectedNewTagColor(box)))
            box.Clear();
    }

    private void TagInput_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (sender is not System.Windows.Controls.TextBox box) return;
        if (e.Key == System.Windows.Input.Key.Escape)
        {
            // Esc closes the label menu
            for (DependencyObject? p = box; p is not null; p = LogicalTreeHelper.GetParent(p))
                if (p is System.Windows.Controls.Primitives.Popup popup) { popup.IsOpen = false; break; }
            e.Handled = true;
            return;
        }
        if (e.Key != System.Windows.Input.Key.Enter) return;
        if (DiskOf(box) is { } d && d.AddTag(box.Text, SelectedNewTagColor(box))) box.Clear();
        e.Handled = true;
    }

    private void TagSuggestion_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: string tag } fe && DiskOf(sender) is { } d)
            d.AddTag(tag, SelectedNewTagColor(fe));
    }

    /// <summary>Esc closes the label and color menus, no matter which element inside has focus.</summary>
    private void TagPopup_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != System.Windows.Input.Key.Escape || sender is not DependencyObject start) return;
        for (DependencyObject? p = start; p is not null; p = LogicalTreeHelper.GetParent(p))
            if (p is System.Windows.Controls.Primitives.Popup popup) { popup.IsOpen = false; e.Handled = true; break; }
    }

    /// <summary>Color of an existing label changed (applies to all drives with this label).</summary>
    private void TagColorEdit_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (e.RemovedItems.Count == 0) return; // initial selection when opening, no change
        if (sender is not System.Windows.Controls.ListBox { DataContext: TagItem tag, SelectedValue: string key } list) return;
        TagPalette.SetColor(tag.Name, key);
        for (DependencyObject? p = list; p is not null; p = LogicalTreeHelper.GetParent(p))
            if (p is System.Windows.Controls.Primitives.Popup popup) { popup.IsOpen = false; break; }
    }

    private void TagPopup_Opened(object? sender, EventArgs e)
    {
        // Focus the input field right away
        if (sender is System.Windows.Controls.Primitives.Popup { Child: DependencyObject root } &&
            FindLogical<System.Windows.Controls.TextBox>(root) is { } box)
            Dispatcher.BeginInvoke(() => { box.Focus(); System.Windows.Input.Keyboard.Focus(box); });
    }

    private static T? FindLogical<T>(DependencyObject parent) where T : DependencyObject
    {
        foreach (var child in LogicalTreeHelper.GetChildren(parent).OfType<DependencyObject>())
        {
            if (child is T match) return match;
            if (FindLogical<T>(child) is { } deeper) return deeper;
        }
        return null;
    }

    /// <summary>Dim the main window behind a dialog.</summary>
    public void SetSmoke(bool on) => Smoke.Visibility = on ? Visibility.Visible : Visibility.Collapsed;

    // ---------- Notice for the "disable" mode (one-time) ----------

    private bool ConfirmDeactivate()
    {
        if (_settings.DeactivateHintShown) return true;
        var answer = FluentDialog.Show(this,
            Loc.T("Laufwerk wirklich deaktivieren?", "Really disable the drive?"),
            Loc.T("Im Modus „Offline + schlafen + deaktivieren“ wird die Platte zusätzlich im Geräte-Manager deaktiviert. " +
                  "Das bleibt auch nach einem Neustart von Windows so.",
                  "In “Offline + spin down + disable” mode the drive is also disabled in Device Manager. " +
                  "This persists across Windows restarts."),
            primary: Loc.T("Modus verwenden", "Use this mode"),
            close: Loc.T("Abbrechen", "Cancel"),
            note: Loc.T("Nutzbar wird die Platte erst wieder, wenn du sie in HDD-Spindown aufweckst oder im Geräte-Manager aktivierst. Dieser Hinweis erscheint nur einmal.",
                        "The drive only becomes usable again once you wake it in HDD-Spindown or enable it in Device Manager. This notice is shown only once."),
            glyph: "");
        if (answer != DialogResultKind.Primary) return false;
        _settings.DeactivateHintShown = true;
        _settings.Save();
        return true;
    }

    // ---------- Adapting to the window width ----------

    public static readonly DependencyProperty LayoutModeProperty =
        DependencyProperty.Register(nameof(LayoutMode), typeof(string), typeof(MainWindow), new PropertyMetadata("Wide"));

    /// <summary>"Wide", "Medium" or "Narrow": controls the cards' control row (see styles in MainWindow.xaml).</summary>
    public string LayoutMode
    {
        get => (string)GetValue(LayoutModeProperty);
        set => SetValue(LayoutModeProperty, value);
    }

    // Thresholds in device-independent pixels (window width)
    private const double WideMin = 960, MediumMin = 760;

    private void UpdateLayoutMode() =>
        LayoutMode = ActualWidth >= WideMin ? "Wide" : ActualWidth >= MediumMin ? "Medium" : "Narrow";

    // ---------- Settings (in the same window) ----------

    private void Settings_Click(object sender, RoutedEventArgs e) => ShowSettings(true);

    private void ShowSettings(bool show)
    {
        if (show) SettingsPanel.Reset();
        SettingsPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        MainView.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
    }

    protected override void OnPreviewKeyDown(System.Windows.Input.KeyEventArgs e)
    {
        // Esc or Alt+Left = back, like in Windows Settings
        bool back = e.Key == System.Windows.Input.Key.Escape
                    || (e.SystemKey == System.Windows.Input.Key.Left && System.Windows.Input.Keyboard.Modifiers == System.Windows.Input.ModifierKeys.Alt);
        if (back && SettingsPanel.IsVisible) { ShowSettings(false); e.Handled = true; }
        base.OnPreviewKeyDown(e);
    }

    private static DiskItem? ItemOf(object sender) => (sender as FrameworkElement)?.Tag as DiskItem;

    // ---------- Window / tray ----------

    public void ShowFromTray()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_exiting)
        {
            // Close = to the notification area; the locks must stay active.
            e.Cancel = true;
            Hide();
            if (!_hintShown)
            {
                _tray.Notify(Loc.T("HDD-Spindown läuft weiter", "HDD-Spindown is still running"), Loc.T("Über das Symbol im Infobereich öffnen oder beenden.", "Open or exit via the icon in the notification area."));
                _hintShown = true;
            }
        }
        base.OnClosing(e);
    }

    /// <summary>Exits the app (or restarts it, e.g. after a language change). Locked drives can be woken first.</summary>
    private async Task ExitAsync(bool restart = false)
    {
        if (_controller.LockedDisks.Count > 0)
        {
            ShowFromTray();
            var answer = FluentDialog.Show(this,
                restart ? Loc.T("HDD-Spindown neu starten", "Restart HDD-Spindown") : Loc.T("HDD-Spindown beenden", "Exit HDD-Spindown"),
                Loc.T("Einige Festplatten sind noch gesperrt oder offline. Sollen sie vorher aufgeweckt werden?",
                      "Some drives are still locked or offline. Should they be woken up first?"),
                primary: Loc.T("Aufwecken", "Wake up"),
                secondary: restart ? Loc.T("Nur neu starten", "Just restart") : Loc.T("Nur beenden", "Just exit"),
                close: Loc.T("Abbrechen", "Cancel"),
                note: Loc.T("Ohne Aufwecken bleiben Offline-Platten bis zum nächsten Windows-Neustart offline, deaktivierte Laufwerke bleiben deaktiviert.",
                            "Without waking, offline drives stay offline until the next Windows restart and disabled drives stay disabled."),
                glyph: "");
            if (answer == DialogResultKind.Close) return;
            if (answer == DialogResultKind.Primary) await _controller.ReleaseAllAsync();
        }

        _exiting = true;
        _powerTimer.Stop();
        _tray.Dispose();
        if (restart) ((App)Application.Current).Restart();
        else Application.Current.Shutdown();
    }

    // ---------- Helpers ----------

    private void ShowInfo(string brushKey, string glyph, string title, string text)
    {
        InfoBar.SetResourceReference(BackgroundProperty, brushKey);
        InfoIcon.Text = glyph;
        InfoTitle.Text = title;
        InfoText.Text = text;
        InfoBar.Visibility = Visibility.Visible;
    }

    private static bool IsElevated()
    {
        using var id = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
    }

    private void Log(string line)
    {
        LogBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {line}{Environment.NewLine}");
        LogBox.ScrollToEnd();
    }
}
