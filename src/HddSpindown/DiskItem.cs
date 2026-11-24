using System.ComponentModel;
using System.Runtime.CompilerServices;
using HddSpindown.Native;
using HddSpindown.Services;

namespace HddSpindown;

public sealed class ModeOption
{
    public required LockMode Mode { get; init; }
    public required string Label { get; init; }
    public override string ToString() => Label;
}

public sealed class DiskItem : INotifyPropertyChanged
{
    public static IReadOnlyList<ModeOption> ModeOptions { get; } =
    [
        new() { Mode = LockMode.SleepOnly, Label = Loc.T("Sperren + schlafen", "Lock + spin down") },
        new() { Mode = LockMode.Offline, Label = Loc.T("Offline + schlafen", "Offline + spin down") },
        new() { Mode = LockMode.Deactivate, Label = Loc.T("Offline + schlafen + deaktivieren", "Offline + spin down + disable") },
    ];

    private readonly DiskSettings _settings;

    public DiskItem(DiskInfo info, DiskSettings settings)
    {
        _info = info;
        _settings = settings;
        Tags = new System.Collections.ObjectModel.ObservableCollection<TagItem>(settings.Tags.Select(t => new TagItem(t)));
    }

    private DiskInfo _info;
    public DiskInfo Info
    {
        get => _info;
        set { _info = value; OnChanged(null); }
    }

    public string InstanceId => Info.InstanceId;
    public string Title => Info.FriendlyName;
    private bool _showTooltip = true;
    /// <summary>Device path as a tooltip over the name (can be turned off in the settings).</summary>
    public bool ShowTooltip { get => _showTooltip; set { if (Set(ref _showTooltip, value)) OnChanged(nameof(ToolTipText)); } }
    public string? ToolTipText => ShowTooltip ? Info.DevicePath ?? Info.InstanceId : null;
    public bool IsSystemDisk => Info.IsSystemDisk;

    /// <summary>Kind for the icon: "Hdd", "Nvme" (M.2 stick) or "Ssd" (2.5-inch SSD).</summary>
    public string Kind => Info.IsNvme ? "Nvme" : !Info.HasSeekPenalty ? "Ssd" : "Hdd";
    public string LettersText => Info.DriveLetters.Any() ? string.Join(", ", Info.DriveLetters) : Loc.T("kein Buchstabe zugeordnet", "no drive letter");

    public string Subtitle
    {
        get
        {
            var parts = new List<string>();
            if (Info.DiskNumber >= 0) parts.Add(Loc.T($"Datenträger {Info.DiskNumber}", $"Disk {Info.DiskNumber}"));
            if (Info.SizeBytes > 0) parts.Add(FormatSize(Info.SizeBytes));
            if (Info.Bus != BusKind.Unknown) parts.Add(Info.Bus.ToString().ToUpperInvariant());
            parts.Add(LettersText);
            if (TimeText is { } t) parts.Add(t);
            return string.Join("  ·  ", parts);
        }
    }

    // ---------- State ----------

    private bool _isLocked;
    /// <summary>Locked/offline/disabled by this app.</summary>
    public bool IsLocked { get => _isLocked; set { if (Set(ref _isLocked, value)) Refresh(); } }

    private PowerState _power;
    public PowerState Power
    {
        get => _power;
        set
        {
            var old = _power;
            if (!Set(ref _power, value)) return;
            // Detect transitions, even if Windows spun the drive down/up by itself.
            if (old == PowerState.Active && value == PowerState.Standby && SleptAt is null) MarkAsleep(DateTime.Now);
            else if (old == PowerState.Standby && value == PowerState.Active) MarkAwake(DateTime.Now);
            Refresh();
        }
    }

    // ---------- Timestamp for the detail line ----------

    public DateTime? SleptAt { get; private set; }
    public DateTime? WokeAt { get; private set; }

    public void MarkAsleep(DateTime at) { SleptAt = at; WokeAt = null; Refresh(); }
    public void MarkAwake(DateTime at) { WokeAt = at; SleptAt = null; Refresh(); }

    /// <summary>"asleep since 14:32" or "woken at 14:32", only while it matches the state.</summary>
    private string? TimeText =>
        SleptAt is { } s && Power != PowerState.Active ? Loc.T($"schläft seit {FormatTime(s)}", $"asleep since {FormatTime(s)}")
        : WokeAt is { } w ? Loc.T($"aufgeweckt um {FormatTime(w)}", $"woken at {FormatTime(w)}")
        : null;

    private static string FormatTime(DateTime t) =>
        t.Date == DateTime.Today ? t.ToString("HH:mm") : t.ToString(Loc.German ? "dd.MM. HH:mm" : "MM/dd HH:mm");

    private bool _isBusy;
    public bool IsBusy { get => _isBusy; set { if (Set(ref _isBusy, value)) Refresh(); } }

    private string _status = "";
    public string Status { get => _status; set => Set(ref _status, value); }

    private string _busyText = "";
    /// <summary>What is happening right now (tooltip/screen reader text of the progress bar).</summary>
    public string BusyText { get => _busyText; set => Set(ref _busyText, value); }

    public string StateText =>
        Info.IsUnavailable ? Loc.T($"Nicht verfügbar (Code {Info.ProblemCode})", $"Unavailable (code {Info.ProblemCode})")
        : Info.IsDisabled ? Loc.T("Deaktiviert", "Disabled")
        : Power == PowerState.Standby ? (Info.IsOffline ? Loc.T("Schläft · offline", "Spun down · offline") : IsLocked ? Loc.T("Schläft · gesperrt", "Spun down · locked") : Loc.T("Schläft", "Spun down"))
        : Info.IsOffline ? "Offline"
        : IsLocked ? Loc.T("Gesperrt", "Locked")
        : Power == PowerState.Active ? Loc.T("Aktiv", "Active")
        : "Online";

    /// <summary>Green = active/online, blue = asleep, grey = offline/disabled.</summary>
    public string StateKind =>
        Info.IsUnavailable || Info.IsDisabled || (Info.IsOffline && Power != PowerState.Standby) ? "Off"
        : Power == PowerState.Standby ? "Sleep"
        : IsLocked ? "Locked"
        : "On";

    /// <summary>Protected (system drive, NVMe SSD): display only, no actions at all.</summary>
    public bool IsReadOnly => Info.IsProtected;
    public bool ShowControls => !IsReadOnly;

    public string? ProtectedBadge =>
        Info.IsSystemDisk ? Loc.T("Systemlaufwerk", "System drive")
        : Info.IsNvme ? Loc.T("NVMe-SSD", "NVMe SSD")
        : Info.SharesProtectedVolume ? Loc.T("Geschützt", "Protected")
        : null;

    public string ProtectedReason =>
        Info.IsNvme ? Loc.T("NVMe-SSD: wird nur angezeigt, nicht gesteuert.", "NVMe SSD: display only, not controlled.")
        : Info.IsSystemDisk ? Loc.T("Systemlaufwerk: wird nur angezeigt, nicht gesteuert.", "System drive: display only, not controlled.")
        : Loc.T("Ein Volume reicht auf eine geschützte Platte, daher nur Anzeige.", "A volume spans a protected drive, so display only.");

    // ---------- Which button is enabled when? ----------
    //  Settings (mode, at startup, labels): not protected, not busy, available.
    //  Actions additionally only with admin rights (they would fail otherwise).
    //  Spin down:    there is still something to do (not already locked AND asleep).
    //  Lock only:    mode with offline/disable, not locked yet and not already offline.
    //  Wake up:      only if the drive is asleep, locked, offline or disabled.
    //  Mode:         cannot be changed while the drive is locked/offline/disabled.

    /// <summary>App runs with admin rights (otherwise all actions are disabled).</summary>
    public static bool CanControl { get; set; } = true;

    public bool CanEdit => !IsReadOnly && !IsBusy && !Info.IsUnavailable;
    private bool CanAct => CanEdit && CanControl;

    public bool CanSleep => CanAct && Info.DevicePath is not null && !(IsLocked && IsAsleep);
    public bool CanLock => CanAct && Info.DevicePath is not null && Mode != LockMode.SleepOnly && !IsLocked && !Info.IsOffline;
    public bool CanWake => CanAct && (NeedsWake || IsAsleep);
    public bool CanChangeMode => CanEdit && !IsLocked && !Info.IsOffline && !Info.IsDisabled;

    /// <summary>Is there anything to wake? (locked, offline, disabled or asleep)</summary>
    public bool NeedsWake => !IsReadOnly && !Info.IsUnavailable &&
        (IsLocked || Info.IsOffline || Info.IsDisabled || Power == PowerState.Standby);

    /// <summary>Currently asleep (according to the query or because the app spun it down).</summary>
    public bool IsAsleep => Power == PowerState.Standby || (SleptAt is not null && Power != PowerState.Active);

    /// <summary>Would "Spin down selected" spin this drive down now?</summary>
    public bool WantsAutoSleep => AutoSleep && CanSleep && !IsLocked;
    public bool ShowLockButton => Info.SupportsStandby;
    public string SleepButtonText => Info.SupportsStandby ? Loc.T("Schlafen", "Spin down") : Loc.T("Sperren", "Lock");

    // ---------- Settings ----------

    /// <summary>
    /// Asked before switching to "disable" in the UI (one-time notice).
    /// false = cancel the change.
    /// </summary>
    public static Func<bool>? ConfirmDeactivate { get; set; }

    public LockMode Mode
    {
        get => _settings.Mode;
        set
        {
            if (_settings.Mode == value) return;
            if (value == LockMode.Deactivate && ConfirmDeactivate is { } confirm && !confirm())
            {
                // Reset the list selection after the binding update
                System.Windows.Application.Current.Dispatcher.BeginInvoke(() => OnChanged(nameof(SelectedMode)));
                return;
            }
            SetMode(value);
        }
    }

    /// <summary>Set the mode without asking (e.g. from the command line).</summary>
    public void SetMode(LockMode value)
    {
        if (_settings.Mode == value) return;
        _settings.Mode = value;
        OnChanged(nameof(Mode)); OnChanged(nameof(SelectedMode)); OnChanged(nameof(CanLock));
        SettingsChanged?.Invoke();
    }

    public ModeOption SelectedMode
    {
        get => ModeOptions.First(o => o.Mode == Mode);
        set { if (value is not null) Mode = value.Mode; }
    }

    public bool AutoSleep
    {
        get => _settings.AutoSleep;
        set
        {
            if (_settings.AutoSleep == value) return;
            _settings.AutoSleep = value;
            OnChanged(nameof(AutoSleep));
            SettingsChanged?.Invoke();
        }
    }

    // ---------- Labels ----------

    public const int MaxTags = 4, MaxTagLength = 20;

    /// <summary>Suggestions in the label menu.</summary>
    public static IReadOnlyList<string> TagSuggestions { get; } = Loc.German
        ? ["Dokumente", "Filme", "Serien", "Musik", "Fotos", "Spiele", "Backup", "Archiv"]
        : ["Documents", "Movies", "TV shows", "Music", "Photos", "Games", "Backup", "Archive"];

    public System.Collections.ObjectModel.ObservableCollection<TagItem> Tags { get; }
    public bool CanAddTag => Tags.Count < MaxTags;
    public string TagsText => string.Join(", ", Tags.Select(t => t.Name));

    /// <param name="colorKey">Color from the palette; null = keep the label's current color.</param>
    public bool AddTag(string? text, string? colorKey = null)
    {
        string tag = (text ?? "").Trim();
        if (tag.Length == 0 || !CanAddTag) return false;
        if (tag.Length > MaxTagLength) tag = tag[..MaxTagLength].TrimEnd();
        if (Tags.Any(t => string.Equals(t.Name, tag, StringComparison.OrdinalIgnoreCase))) return false;
        if (colorKey is not null) TagPalette.SetColor(tag, colorKey);
        Tags.Add(new TagItem(tag));
        SaveTags();
        return true;
    }

    public void RemoveTag(TagItem tag)
    {
        if (Tags.Remove(tag)) SaveTags();
    }

    /// <summary>Recolor all pills after a color change.</summary>
    public void RefreshTagColors()
    {
        foreach (var t in Tags) t.Refresh();
    }

    private void SaveTags()
    {
        _settings.Tags = Tags.Select(t => t.Name).ToList();
        OnChanged(nameof(CanAddTag)); OnChanged(nameof(TagsText));
        SettingsChanged?.Invoke();
    }

    public event Action? SettingsChanged;
    public event PropertyChangedEventHandler? PropertyChanged;

    private void Refresh() => OnChanged(null); // re-read all derived properties

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnChanged(name);
        return true;
    }

    private void OnChanged(string? name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private static string FormatSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double v = bytes;
        int i = 0;
        while (v >= 1000 && i < units.Length - 1) { v /= 1000; i++; }
        return $"{v:0.#} {units[i]}";
    }
}
