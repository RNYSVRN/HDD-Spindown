using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using static HddSpindown.Native.NativeMethods;

namespace HddSpindown.Native;

public enum PowerState { Unknown, Active, Standby }

/// <summary>
/// Low-level operations on drives. All methods require admin rights.
/// Errors are thrown as Win32Exception with a readable message.
/// </summary>
public static class DiskControl
{
    private const byte ATA_STANDBY_IMMEDIATE = 0xE0;
    private const byte ATA_CHECK_POWER_MODE = 0xE5;
    private const byte SCSI_START_STOP_UNIT = 0x1B;

    // ---------- Locking volumes ----------

    /// <summary>
    /// Locks and dismounts a volume. The returned handle holds the lock
    /// as long as it is open, so nobody else can access the volume.
    /// </summary>
    public static SafeFileHandle LockAndDismountVolume(VolumeInfo volume)
    {
        var h = Open(volume.DevicePath, GENERIC_READ | GENERIC_WRITE);
        if (h.IsInvalid)
            throw new Win32Exception(Marshal.GetLastWin32Error(), Loc.T($"Volume {Label(volume)} konnte nicht geöffnet werden", $"Could not open volume {Label(volume)}"));

        // Locking fails while programs have files open on the volume.
        // We deliberately do NOT force it, to avoid losing unsaved data.
        if (!Ioctl(h, FSCTL_LOCK_VOLUME))
        {
            int err = Marshal.GetLastWin32Error();
            h.Dispose();
            throw new VolumeInUseException(err, Loc.T($"Volume {Label(volume)} ist in Benutzung und kann nicht gesperrt werden", $"Volume {Label(volume)} is in use and cannot be locked"));
        }
        if (!Ioctl(h, FSCTL_DISMOUNT_VOLUME))
        {
            int err = Marshal.GetLastWin32Error();
            Ioctl(h, FSCTL_UNLOCK_VOLUME);
            h.Dispose();
            throw new Win32Exception(err, Loc.T($"Volume {Label(volume)} konnte nicht ausgehängt werden", $"Could not dismount volume {Label(volume)}"));
        }
        return h;
    }

    public static void UnlockVolume(SafeFileHandle h)
    {
        if (h.IsInvalid || h.IsClosed) return;
        Ioctl(h, FSCTL_UNLOCK_VOLUME);
        h.Dispose();
    }

    private static string Label(VolumeInfo v) => v.DriveLetter ?? v.VolumeName;

    // ---------- Online / Offline ----------

    /// <summary>Sets the drive offline/online. Not persistent: it is online again after a reboot.</summary>
    public static unsafe void SetOffline(string diskPath, bool offline)
    {
        using var h = OpenDisk(diskPath);
        var a = new SET_DISK_ATTRIBUTES
        {
            Version = (uint)sizeof(SET_DISK_ATTRIBUTES),
            Persist = 0,
            Attributes = offline ? DISK_ATTRIBUTE_OFFLINE : 0,
            AttributesMask = DISK_ATTRIBUTE_OFFLINE,
        };
        if (!DeviceIoControl(h, IOCTL_DISK_SET_DISK_ATTRIBUTES, &a, (uint)sizeof(SET_DISK_ATTRIBUTES), null, 0, out _, IntPtr.Zero))
            throw new Win32Exception(Marshal.GetLastWin32Error(),
                offline ? Loc.T("Datenträger konnte nicht offline geschaltet werden", "Could not take drive offline") : Loc.T("Datenträger konnte nicht online geschaltet werden", "Could not bring drive online"));
        Ioctl(h, IOCTL_DISK_UPDATE_PROPERTIES);
    }

    public static bool IsOffline(string diskPath)
    {
        using var h = Open(diskPath, GENERIC_READ);
        return !h.IsInvalid && DiskEnumerator.IsDiskOffline(h);
    }

    // ---------- Spin down / wake up ----------

    public static void Standby(string diskPath, BusKind bus)
    {
        using var h = OpenDisk(diskPath);
        switch (bus)
        {
            case BusKind.Ata:
                Ata(h, ATA_STANDBY_IMMEDIATE, out _);
                break;
            case BusKind.Scsi:
            case BusKind.Usb:
                // START STOP UNIT with START=0 -> stop the motor
                Scsi(h, [SCSI_START_STOP_UNIT, 0, 0, 0, 0x00, 0]);
                break;
            default:
                throw new NotSupportedException(Loc.T("Für diesen Datenträgertyp (z. B. NVMe-SSD) gibt es keinen Schlafbefehl.", "There is no spin-down command for this drive type (e.g. NVMe SSD)."));
        }
    }

    /// <summary>Wakes the drive by reading the first sector (or START UNIT for SCSI/USB).</summary>
    public static unsafe void SpinUp(string diskPath, BusKind bus)
    {
        using var h = Open(diskPath, GENERIC_READ | GENERIC_WRITE, FILE_FLAG_NO_BUFFERING);
        if (h.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error(), Loc.T("Datenträger konnte nicht geöffnet werden", "Could not open drive"));

        if (bus is BusKind.Scsi or BusKind.Usb)
        {
            try { Scsi(h, [SCSI_START_STOP_UNIT, 0, 0, 0, 0x01, 0]); } catch { /* some bridges don't support it */ }
        }

        const uint len = 4096;
        void* buf = NativeMemory.AlignedAlloc(len, 4096);
        try { ReadFile(h, buf, len, out _, IntPtr.Zero); }
        finally { NativeMemory.AlignedFree(buf); }
    }

    /// <summary>Queries the power state without waking the drive (ATA/SATA only).</summary>
    public static PowerState GetPowerState(string diskPath, BusKind bus)
    {
        if (bus != BusKind.Ata) return PowerState.Unknown;
        using var h = Open(diskPath, GENERIC_READ | GENERIC_WRITE);
        if (h.IsInvalid) return PowerState.Unknown;
        try
        {
            Ata(h, ATA_CHECK_POWER_MODE, out byte sectorCount);
            return sectorCount == 0x00 ? PowerState.Standby : PowerState.Active;
        }
        catch { return PowerState.Unknown; }
    }

    // ---------- Pass-Through ----------

    private static unsafe void Ata(SafeFileHandle h, byte command, out byte sectorCountOut)
    {
        var apt = new ATA_PASS_THROUGH_EX
        {
            Length = (ushort)sizeof(ATA_PASS_THROUGH_EX),
            AtaFlags = ATA_FLAGS_DRDY_REQUIRED,
            TimeOutValue = 15,
        };
        apt.CurrentTaskFile[6] = command;
        if (!DeviceIoControl(h, IOCTL_ATA_PASS_THROUGH, &apt, (uint)sizeof(ATA_PASS_THROUGH_EX),
                &apt, (uint)sizeof(ATA_PASS_THROUGH_EX), out _, IntPtr.Zero))
            throw new Win32Exception(Marshal.GetLastWin32Error(), Loc.T($"ATA-Befehl 0x{command:X2} fehlgeschlagen", $"ATA command 0x{command:X2} failed"));

        // Status register (index 6) bit 0 = ERR
        if ((apt.CurrentTaskFile[6] & 0x01) != 0)
            throw new Win32Exception(0, Loc.T($"Festplatte hat ATA-Befehl 0x{command:X2} abgelehnt", $"Drive rejected ATA command 0x{command:X2}"));
        sectorCountOut = apt.CurrentTaskFile[1];
    }

    private static unsafe void Scsi(SafeFileHandle h, byte[] cdb)
    {
        var s = new SCSI_PASS_THROUGH_WITH_SENSE();
        s.Spt.Length = (ushort)sizeof(SCSI_PASS_THROUGH);
        s.Spt.CdbLength = (byte)cdb.Length;
        s.Spt.SenseInfoLength = 32;
        s.Spt.DataIn = SCSI_IOCTL_DATA_UNSPECIFIED;
        s.Spt.TimeOutValue = 30;
        s.Spt.SenseInfoOffset = (uint)sizeof(SCSI_PASS_THROUGH);
        for (int i = 0; i < cdb.Length; i++) s.Spt.Cdb[i] = cdb[i];

        if (!DeviceIoControl(h, IOCTL_SCSI_PASS_THROUGH, &s, (uint)sizeof(SCSI_PASS_THROUGH_WITH_SENSE),
                &s, (uint)sizeof(SCSI_PASS_THROUGH_WITH_SENSE), out _, IntPtr.Zero))
            throw new Win32Exception(Marshal.GetLastWin32Error(), Loc.T("SCSI-Befehl fehlgeschlagen", "SCSI command failed"));
        if (s.Spt.ScsiStatus != 0)
            throw new Win32Exception(0, Loc.T($"Gerät meldet SCSI-Status 0x{s.Spt.ScsiStatus:X2}", $"Device reported SCSI status 0x{s.Spt.ScsiStatus:X2}"));
    }

    private static SafeFileHandle OpenDisk(string diskPath)
    {
        var h = Open(diskPath, GENERIC_READ | GENERIC_WRITE);
        if (h.IsInvalid)
            throw new Win32Exception(Marshal.GetLastWin32Error(), Loc.T("Datenträger konnte nicht geöffnet werden (Adminrechte?)", "Could not open drive (administrator rights?)"));
        return h;
    }
}

/// <summary>FSCTL_LOCK_VOLUME failed: programs hold files open on the volume.</summary>
public sealed class VolumeInUseException(int error, string message) : Win32Exception(error, message);
