using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using static HddSpindown.Native.NativeMethods;

namespace HddSpindown.Native;

public enum BusKind { Unknown, Ata, Scsi, Usb, Nvme, Other }

public sealed record VolumeInfo(string VolumeName, IReadOnlyList<string> MountPoints, IReadOnlyList<int> DiskNumbers)
{
    /// <summary>Path without a trailing backslash, for opening with CreateFile.</summary>
    public string DevicePath => VolumeName.TrimEnd('\\');
    public string? DriveLetter => MountPoints.FirstOrDefault(m => m.Length == 3 && m[1] == ':')?.Substring(0, 2);
}

public sealed record DiskInfo(
    string InstanceId,
    string FriendlyName,
    string? DevicePath,          // null if the device is disabled
    int DiskNumber,
    long SizeBytes,
    BusKind Bus,
    bool IsDisabled,
    uint ProblemCode,            // Device Manager error code, 0 = OK
    bool IsOffline,
    IReadOnlyList<VolumeInfo> Volumes,
    bool IsSystemDisk,
    bool HasSeekPenalty)          // true = spinning drive (HDD), false = SSD
{
    public IEnumerable<string> DriveLetters => Volumes.Select(v => v.DriveLetter).OfType<string>().Order(StringComparer.Ordinal);
    public bool SupportsStandby => Bus is BusKind.Ata or BusKind.Scsi or BusKind.Usb;

    /// <summary>
    /// NVMe SSD: only displayed, never modified. Checked twice: bus type OR
    /// a hint in the instance ID/name, so nothing happens even with an unknown bus type.
    /// </summary>
    public bool IsNvme =>
        Bus == BusKind.Nvme
        || InstanceId.Contains("NVME", StringComparison.OrdinalIgnoreCase)
        || FriendlyName.Contains("NVME", StringComparison.OrdinalIgnoreCase);

    /// <summary>A volume of this drive also extends onto a protected drive (e.g. spanned volume).</summary>
    public bool SharesProtectedVolume { get; init; }

    /// <summary>Must never be modified by this app.</summary>
    public bool IsProtected => IsSystemDisk || IsNvme || SharesProtectedVolume;
    /// <summary>Device has an error other than "disabled" (e.g. code 21) and cannot be accessed.</summary>
    public bool IsUnavailable => !IsDisabled && ProblemCode != 0 && DevicePath is null;
}

/// <summary>
/// Lists all drives of the "Disk drives" device class, including disabled ones,
/// and assigns volumes, bus type and status to them. Read-only queries.
/// </summary>
public static class DiskEnumerator
{
    public static List<DiskInfo> GetDisks()
    {
        var volumesByDisk = GetVolumesByDisk();
        string sysLetter = Path.GetPathRoot(Environment.SystemDirectory)!.Substring(0, 2).ToUpperInvariant();

        var result = new List<DiskInfo>();
        var classGuid = GUID_DEVCLASS_DISKDRIVE;
        IntPtr set = SetupDiGetClassDevs(ref classGuid, IntPtr.Zero, IntPtr.Zero, DIGCF_PRESENT);
        if (set == new IntPtr(-1)) throw new Win32Exception();

        try
        {
            for (int i = 0; ; i++)
            {
                var dev = new SP_DEVINFO_DATA { cbSize = Marshal.SizeOf<SP_DEVINFO_DATA>() };
                if (!SetupDiEnumDeviceInfo(set, i, ref dev))
                {
                    if (Marshal.GetLastWin32Error() == ERROR_NO_MORE_ITEMS) break;
                    throw new Win32Exception();
                }

                string? instanceId = GetInstanceId(set, ref dev);
                if (instanceId is null) continue;

                string name = GetProperty(set, ref dev, SPDRP_FRIENDLYNAME)
                              ?? GetProperty(set, ref dev, SPDRP_DEVICEDESC)
                              ?? Loc.T("Unbekannter Datenträger", "Unknown drive");

                uint problemCode = CM_Get_DevNode_Status(out uint status, out uint problem, dev.DevInst, 0) == CR_SUCCESS
                                   && (status & DN_HAS_PROBLEM) != 0 ? problem : 0;
                bool disabled = problemCode == CM_PROB_DISABLED;

                string? path = disabled ? null : GetDiskInterfacePath(instanceId);
                int number = -1;
                long size = 0;
                var bus = BusKind.Unknown;
                bool seekPenalty = true;
                bool offline = false;

                if (path is not null)
                {
                    using (var h = Open(path, 0))
                        if (!h.IsInvalid) { number = GetDeviceNumber(h); bus = GetBusKind(h); seekPenalty = GetSeekPenalty(h) ?? true; }
                    using (var h = Open(path, GENERIC_READ))
                        if (!h.IsInvalid) { size = GetLength(h); offline = IsDiskOffline(h); }
                }

                var volumes = number >= 0 && volumesByDisk.TryGetValue(number, out var v) ? v : new List<VolumeInfo>();
                bool isSystem = volumes.Any(x => string.Equals(x.DriveLetter, sysLetter, StringComparison.OrdinalIgnoreCase));

                result.Add(new DiskInfo(instanceId, name, path, number, size, bus, disabled, problemCode, offline, volumes, isSystem, seekPenalty));
            }
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(set);
        }

        // Also protect drives whose volumes also reside on a protected drive.
        var protectedNumbers = result.Where(d => d.IsProtected && d.DiskNumber >= 0).Select(d => d.DiskNumber).ToHashSet();
        for (int i = 0; i < result.Count; i++)
        {
            var d = result[i];
            if (!d.IsProtected && d.Volumes.Any(v => v.DiskNumbers.Any(protectedNumbers.Contains)))
                result[i] = d with { SharesProtectedVolume = true };
        }

        // Sorting: by first drive letter, drives without a letter after that (by number).
        return result
            .OrderBy(d => d.DriveLetters.FirstOrDefault() ?? "~", StringComparer.Ordinal)
            .ThenBy(d => d.DiskNumber < 0 ? int.MaxValue : d.DiskNumber)
            .ToList();
    }

    /// <summary>Device path (\\?\...#{53f56307-...}) for an instance ID, once it is available.</summary>
    public static string? GetDiskInterfacePath(string instanceId)
    {
        var guid = GUID_DEVINTERFACE_DISK;
        if (CM_Get_Device_Interface_List_Size(out int len, ref guid, instanceId, CM_GET_DEVICE_INTERFACE_LIST_PRESENT) != CR_SUCCESS || len <= 1)
            return null;
        var buf = new char[len];
        if (CM_Get_Device_Interface_List(ref guid, instanceId, buf, len, CM_GET_DEVICE_INTERFACE_LIST_PRESENT) != CR_SUCCESS)
            return null;
        string first = new string(buf).Split('\0', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        return first.Length > 0 ? first : null;
    }

    private static Dictionary<int, List<VolumeInfo>> GetVolumesByDisk()
    {
        var map = new Dictionary<int, List<VolumeInfo>>();
        var name = new char[260];
        IntPtr find = FindFirstVolume(name, name.Length);
        if (find == new IntPtr(-1)) return map;
        try
        {
            do
            {
                int end = Array.IndexOf(name, '\0');
                string volName = new string(name, 0, end >= 0 ? end : name.Length);
                var disks = GetVolumeDisks(volName.TrimEnd('\\'));
                var vol = new VolumeInfo(volName, GetMountPoints(volName), disks);
                foreach (int disk in disks)
                {
                    if (!map.TryGetValue(disk, out var list)) map[disk] = list = new List<VolumeInfo>();
                    list.Add(vol);
                }
                Array.Clear(name);
            } while (FindNextVolume(find, name, name.Length));
        }
        finally { FindVolumeClose(find); }
        return map;
    }

    private static List<string> GetMountPoints(string volumeName)
    {
        var buf = new char[1024];
        if (!GetVolumePathNamesForVolumeName(volumeName, buf, buf.Length, out _)) return new();
        return new string(buf).Split('\0', StringSplitOptions.RemoveEmptyEntries).ToList();
    }

    private static unsafe List<int> GetVolumeDisks(string volumeDevicePath)
    {
        var disks = new List<int>();
        using var h = Open(volumeDevicePath, 0);
        if (h.IsInvalid) return disks;

        const int size = 8 + 24 * 32;
        byte* buf = stackalloc byte[size];
        if (!DeviceIoControl(h, IOCTL_VOLUME_GET_VOLUME_DISK_EXTENTS, null, 0, buf, size, out _, IntPtr.Zero))
            return disks;
        uint count = *(uint*)buf;
        for (int i = 0; i < count && i < 32; i++)
        {
            int disk = (int)*(uint*)(buf + 8 + i * 24);
            if (!disks.Contains(disk)) disks.Add(disk);
        }
        return disks;
    }

    internal static unsafe int GetDeviceNumber(Microsoft.Win32.SafeHandles.SafeFileHandle h)
    {
        STORAGE_DEVICE_NUMBER sdn;
        return DeviceIoControl(h, IOCTL_STORAGE_GET_DEVICE_NUMBER, null, 0, &sdn, (uint)sizeof(STORAGE_DEVICE_NUMBER), out _, IntPtr.Zero)
            ? (int)sdn.DeviceNumber : -1;
    }

    private static unsafe long GetLength(Microsoft.Win32.SafeHandles.SafeFileHandle h)
    {
        long len;
        return DeviceIoControl(h, IOCTL_DISK_GET_LENGTH_INFO, null, 0, &len, sizeof(long), out _, IntPtr.Zero) ? len : 0;
    }

    internal static unsafe bool IsDiskOffline(Microsoft.Win32.SafeHandles.SafeFileHandle h)
    {
        var a = new GET_DISK_ATTRIBUTES { Version = (uint)sizeof(GET_DISK_ATTRIBUTES) };
        return DeviceIoControl(h, IOCTL_DISK_GET_DISK_ATTRIBUTES, null, 0, &a, (uint)sizeof(GET_DISK_ATTRIBUTES), out _, IntPtr.Zero)
               && (a.Attributes & DISK_ATTRIBUTE_OFFLINE) != 0;
    }

    /// <summary>StorageDeviceSeekPenaltyProperty: false = SSD. null if it cannot be queried.</summary>
    private static unsafe bool? GetSeekPenalty(Microsoft.Win32.SafeHandles.SafeFileHandle h)
    {
        var q = new STORAGE_PROPERTY_QUERY { PropertyId = 7, QueryType = 0 };
        byte* buf = stackalloc byte[12];
        if (!DeviceIoControl(h, IOCTL_STORAGE_QUERY_PROPERTY, &q, (uint)sizeof(STORAGE_PROPERTY_QUERY), buf, 12, out uint got, IntPtr.Zero) || got < 9)
            return null;
        return buf[8] != 0; // DEVICE_SEEK_PENALTY_DESCRIPTOR.IncursSeekPenalty
    }

    private static unsafe BusKind GetBusKind(Microsoft.Win32.SafeHandles.SafeFileHandle h)
    {
        var q = new STORAGE_PROPERTY_QUERY { PropertyId = 0 /*StorageDeviceProperty*/, QueryType = 0 /*Standard*/ };
        const int size = 1024;
        byte* buf = stackalloc byte[size];
        if (!DeviceIoControl(h, IOCTL_STORAGE_QUERY_PROPERTY, &q, (uint)sizeof(STORAGE_PROPERTY_QUERY), buf, size, out uint got, IntPtr.Zero) || got < 32)
            return BusKind.Unknown;
        int busType = *(int*)(buf + 28); // STORAGE_DEVICE_DESCRIPTOR.BusType
        return busType switch
        {
            3 or 11 => BusKind.Ata,          // BusTypeAta, BusTypeSata
            1 or 10 => BusKind.Scsi,         // BusTypeScsi, BusTypeSas
            7 => BusKind.Usb,                // BusTypeUsb
            17 => BusKind.Nvme,              // BusTypeNvme
            0 => BusKind.Unknown,
            _ => BusKind.Other,
        };
    }

    private static string? GetInstanceId(IntPtr set, ref SP_DEVINFO_DATA dev)
    {
        var buf = new char[512];
        return SetupDiGetDeviceInstanceId(set, ref dev, buf, buf.Length, out int req)
            ? new string(buf, 0, Math.Max(0, req - 1)) : null;
    }

    private static string? GetProperty(IntPtr set, ref SP_DEVINFO_DATA devInfo, int prop)
    {
        var buf = new byte[1024];
        if (!SetupDiGetDeviceRegistryProperty(set, ref devInfo, prop, out _, buf, buf.Length, out int size))
            return null;
        var s = System.Text.Encoding.Unicode.GetString(buf, 0, size).TrimEnd('\0');
        return string.IsNullOrWhiteSpace(s) ? null : s;
    }
}
