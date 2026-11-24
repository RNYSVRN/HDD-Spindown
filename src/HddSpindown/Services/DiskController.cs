using Microsoft.Win32.SafeHandles;
using HddSpindown.Native;

namespace HddSpindown.Services;

/// <summary>
/// Orchestrates spin down / lock / wake up. Holds the volume locks
/// while a drive is asleep, so Windows doesn't spin it up again right away.
/// Only one action runs at a time.
/// </summary>
public sealed class DiskController
{
    private sealed class Session
    {
        public required DiskInfo Disk { get; init; }
        public List<SafeFileHandle> Locks { get; } = new();
        public bool Offlined { get; set; }
        public bool Disabled { get; set; }
        public bool Asleep { get; set; }
    }

    private readonly Dictionary<string, Session> _sessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _gate = new(1, 1);

    public event Action<string>? Log;
    public event Action? StateChanged;

    public bool IsLocked(string instanceId) => _sessions.ContainsKey(instanceId);
    public bool IsAsleep(string instanceId) => _sessions.TryGetValue(instanceId, out var s) && s.Asleep;
    public IReadOnlyCollection<string> LockedDisks => _sessions.Keys.ToList();

    /// <summary>Locks the drive according to the mode and (with <paramref name="sleep"/>) spins it down.</summary>
    public Task SleepAsync(DiskInfo disk, LockMode mode, bool sleep = true) => Exclusive(async () =>
    {
        EnsureNotProtected(disk);
        string name = disk.FriendlyName;

        // Already locked (e.g. "Lock only" first)? Then just send the spin-down command.
        if (_sessions.TryGetValue(disk.InstanceId, out var existing))
        {
            if (sleep)
            {
                await Task.Run(() => SpinDownSession(existing));
                existing.Asleep = true;
                Write(Loc.T($"{name}: schlafen gelegt.", $"{name}: spun down."));
            }
            return;
        }

        if (disk.DevicePath is null) throw new InvalidOperationException(Loc.T("Laufwerk ist deaktiviert, zuerst aufwecken.", "Drive is disabled, wake it up first."));
        string path = disk.DevicePath;

        var s = new Session { Disk = disk };
        await BatchHooks.RunAsync(BatchHooks.BeforeSleep, disk, disk.Volumes, Write);

        try
        {
            await Task.Run(() =>
            {
                // If the drive is already offline, its volumes are unreachable (and unusable),
                // so there is nothing to lock.
                foreach (var v in disk.IsOffline ? [] : disk.Volumes)
                {
                    try
                    {
                        s.Locks.Add(DiskControl.LockAndDismountVolume(v));
                    }
                    catch (VolumeInUseException ex)
                    {
                        // In use: find out which programs hold files open on the drive
                        Write(Loc.T($"{name}: Volume {v.DriveLetter ?? v.VolumeName} ist in Benutzung, suche Programme …",
                                    $"{name}: volume {v.DriveLetter ?? v.VolumeName} is in use, looking for programs …"));
                        var procs = OpenFileFinder.Find(disk.Volumes, TimeSpan.FromSeconds(8));
                        foreach (var p in procs)
                            Write($"  • {p}" + (p.ExamplePath is { } ep ? $"  ({ShortPath(ep, v)})" : ""));
                        string label = v.DriveLetter ?? Loc.T("Ein Volume", "A volume");
                        // Only name Explorer in the short message if nothing else was found
                        var named = procs.Any(p => !p.UsuallyReleases) ? procs.Where(p => !p.UsuallyReleases).ToList() : procs;
                        string msg = procs.Count > 0
                            ? Loc.T($"{label} wird noch verwendet von: {string.Join(", ", named.Select(p => p.DisplayName))}",
                                    $"{label} is still in use by: {string.Join(", ", named.Select(p => p.DisplayName))}")
                            : Loc.T($"{label} ist in Benutzung, das Programm ließ sich nicht ermitteln (z. B. ein Dienst oder Windows selbst).",
                                    $"{label} is in use; the program could not be identified (e.g. a service or Windows itself).");
                        throw new DiskInUseException(msg, procs, ex);
                    }
                    Write(Loc.T($"{name}: Volume {v.DriveLetter ?? v.VolumeName} gesperrt und ausgehängt.", $"{name}: volume {v.DriveLetter ?? v.VolumeName} locked and dismounted."));
                }

                if (mode >= LockMode.Offline)
                {
                    DiskControl.SetOffline(path, true);
                    s.Offlined = true;
                    // After going offline, the volume handles are useless.
                    foreach (var h in s.Locks) h.Dispose();
                    s.Locks.Clear();
                    Write(Loc.T($"{name}: offline geschaltet.", $"{name}: taken offline."));
                }

                if (sleep)
                {
                    if (disk.SupportsStandby)
                    {
                        DiskControl.Standby(path, disk.Bus);
                        s.Asleep = true;
                        Write(Loc.T($"{name}: schlafen gelegt.", $"{name}: spun down."));
                    }
                    else
                    {
                        Write(Loc.T($"{name}: kein Schlafbefehl für diesen Typ ({disk.Bus}), nur gesperrt.", $"{name}: no spin-down command for this type ({disk.Bus}), locked only."));
                    }
                }

                // Disable last: a disabled drive can no longer be reached by anyone, not even by this app.
                // Disabling does not wake a sleeping drive (checked on real hardware).
                if (mode == LockMode.Deactivate)
                {
                    s.Disabled = true;   // set first, so a rollback re-enables it in any case
                    DeviceControl.Disable(disk.InstanceId);
                    Write(Loc.T($"{name}: Gerät deaktiviert (von Windows bestätigt), für Windows und alle Programme nicht mehr erreichbar.",
                                $"{name}: device disabled (confirmed by Windows), no longer reachable for Windows and all programs."));
                }
            });
        }
        catch
        {
            // Roll back so the drive isn't left half locked.
            await Task.Run(() => Rollback(s));
            throw;
        }

        _sessions[disk.InstanceId] = s;
    });

    /// <summary>Releases all locks and wakes the drive.</summary>
    public Task WakeAsync(DiskInfo disk) => Exclusive(async () =>
    {
        EnsureNotProtected(disk);
        _sessions.TryGetValue(disk.InstanceId, out var s);
        string name = disk.FriendlyName;
        var volumes = s?.Disk.Volumes is { Count: > 0 } known ? known : disk.Volumes;

        await Task.Run(() =>
        {
            if (s?.Disabled == true || disk.IsDisabled)
            {
                DeviceControl.Enable(disk.InstanceId);
                Write(Loc.T($"{name}: Gerät aktiviert.", $"{name}: device enabled."));
            }

            string path = WaitForInterface(disk.InstanceId, TimeSpan.FromSeconds(15))
                          ?? throw new InvalidOperationException(Loc.T("Laufwerk ist nach dem Aktivieren nicht wieder aufgetaucht.", "Drive did not reappear after enabling."));

            if (s?.Offlined == true || DiskControl.IsOffline(path))
            {
                DiskControl.SetOffline(path, false);
                Write(Loc.T($"{name}: online geschaltet.", $"{name}: brought online."));
            }

            if (s is not null)
            {
                foreach (var h in s.Locks) DiskControl.UnlockVolume(h);
                if (s.Locks.Count > 0) Write(Loc.T($"{name}: Volumes entsperrt.", $"{name}: volumes unlocked."));
                s.Locks.Clear();
            }

            var bus = s?.Disk.Bus ?? disk.Bus;
            DiskControl.SpinUp(path, bus);
            Write(Loc.T($"{name}: aufgeweckt.", $"{name}: woken up."));
        });

        _sessions.Remove(disk.InstanceId);
        await BatchHooks.RunAsync(BatchHooks.AfterSleep, disk, volumes, Write);
    });

    /// <summary>
    /// Sends the spin-down command for a drive that is already locked. A disabled drive can't be reached,
    /// so it is enabled briefly (still offline and locked), spun down and disabled again.
    /// </summary>
    private void SpinDownSession(Session s)
    {
        if (!s.Disabled)
        {
            DiskControl.Standby(s.Disk.DevicePath ?? throw new InvalidOperationException(Loc.T("Laufwerk ist nicht erreichbar.", "Drive is not reachable.")), s.Disk.Bus);
            return;
        }
        DeviceControl.Enable(s.Disk.InstanceId);
        string path = WaitForInterface(s.Disk.InstanceId, TimeSpan.FromSeconds(15))
                      ?? throw new InvalidOperationException(Loc.T("Laufwerk ist nach dem Aktivieren nicht wieder aufgetaucht.", "Drive did not reappear after enabling."));
        if (s.Offlined && !DiskControl.IsOffline(path)) DiskControl.SetOffline(path, true);
        DiskControl.Standby(path, s.Disk.Bus);
        DeviceControl.Disable(s.Disk.InstanceId);
    }

    /// <summary>
    /// After the PC itself was in standby, every drive gets power again and spins up, no matter what was
    /// set before. This restores the sleep state of the drives this app put to sleep (and only those).
    /// </summary>
    public Task ReapplySleepAfterResumeAsync() => Exclusive(async () =>
    {
        foreach (var s in _sessions.Values.Where(x => x.Asleep).ToList())
        {
            string name = s.Disk.FriendlyName;
            try
            {
                await Task.Run(() =>
                {
                    // An enabled drive can be checked first; a disabled one can't be reached, so it is assumed running
                    if (!s.Disabled && s.Disk.DevicePath is { } p && DiskControl.GetPowerState(p, s.Disk.Bus) == PowerState.Standby) return;
                    SpinDownSession(s);
                    Write(Loc.T($"{name}: lief nach dem Energiesparmodus des PCs, wieder schlafen gelegt.",
                                $"{name}: was running after the PC resumed from sleep, spun down again."));
                });
            }
            catch (Exception ex) { Write($"{name}: {ex.Message}"); }
        }
    });

    /// <summary>On exit, releases all locks and brings offline drives back online.</summary>
    public async Task ReleaseAllAsync()
    {
        foreach (var s in _sessions.Values.ToList())
        {
            try { await WakeAsync(s.Disk); }
            catch (Exception ex) { Write($"{s.Disk.FriendlyName}: {ex.Message}"); }
        }
    }

    /// <summary>Last line of defense: no matter whether called from the UI, tray or command line.</summary>
    private static void EnsureNotProtected(DiskInfo disk)
    {
        if (disk.IsNvme) throw new InvalidOperationException(Loc.T("NVMe-SSDs werden nur angezeigt und nie verändert.", "NVMe SSDs are display-only and never modified."));
        if (disk.IsSystemDisk) throw new InvalidOperationException(Loc.T("Das Systemlaufwerk wird nie verändert.", "The system drive is never modified."));
        if (disk.IsProtected) throw new InvalidOperationException(Loc.T("Ein Volume dieser Platte liegt auch auf einer geschützten Platte.", "A volume on this drive also spans a protected drive."));
    }

    private void Rollback(Session s)
    {
        try
        {
            if (s.Disabled && DeviceControl.IsReallyDisabled(s.Disk.InstanceId)) DeviceControl.Enable(s.Disk.InstanceId);
            if (s.Offlined && s.Disk.DevicePath is { } p) DiskControl.SetOffline(p, false);
        }
        catch (Exception ex) { Write(Loc.T($"Zurücksetzen fehlgeschlagen: {ex.Message}", $"Rollback failed: {ex.Message}")); }
        foreach (var h in s.Locks) DiskControl.UnlockVolume(h);
        s.Locks.Clear();
    }

    private static string? WaitForInterface(string instanceId, TimeSpan timeout)
    {
        var until = DateTime.UtcNow + timeout;
        while (true)
        {
            var p = DiskEnumerator.GetDiskInterfacePath(instanceId);
            if (p is not null || DateTime.UtcNow > until) return p;
            Thread.Sleep(250);
        }
    }

    private async Task Exclusive(Func<Task> action)
    {
        await _gate.WaitAsync();
        try { await action(); }
        finally
        {
            _gate.Release();
            StateChanged?.Invoke();
        }
    }

    private void Write(string line) => Log?.Invoke(line);

    /// <summary>\\?\Volume{GUID}\Folder\File -> J:\Folder\File (for the log).</summary>
    private static string ShortPath(string path, VolumeInfo v)
    {
        string prefix = v.VolumeName.TrimEnd('\\') + "\\";
        return v.DriveLetter is { } letter && path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? letter + "\\" + path[prefix.Length..]
            : path;
    }
}
