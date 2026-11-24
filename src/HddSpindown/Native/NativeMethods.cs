using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace HddSpindown.Native;

internal static class NativeMethods
{
    // ---------- File / IOCTL ----------
    public const uint GENERIC_READ = 0x80000000, GENERIC_WRITE = 0x40000000;
    public const uint FILE_SHARE_READ = 1, FILE_SHARE_WRITE = 2;
    public const uint OPEN_EXISTING = 3;
    public const uint FILE_FLAG_NO_BUFFERING = 0x20000000;

    public const uint IOCTL_STORAGE_GET_DEVICE_NUMBER = 0x002D1080;
    public const uint IOCTL_STORAGE_QUERY_PROPERTY = 0x002D1400;
    public const uint IOCTL_DISK_GET_LENGTH_INFO = 0x0007405C;
    public const uint IOCTL_DISK_GET_DISK_ATTRIBUTES = 0x000700F0;
    public const uint IOCTL_DISK_SET_DISK_ATTRIBUTES = 0x0007C0F4;
    public const uint IOCTL_DISK_UPDATE_PROPERTIES = 0x00070140;
    public const uint IOCTL_VOLUME_GET_VOLUME_DISK_EXTENTS = 0x00560000;
    public const uint IOCTL_ATA_PASS_THROUGH = 0x0004D02C;
    public const uint IOCTL_SCSI_PASS_THROUGH = 0x0004D004;
    public const uint FSCTL_LOCK_VOLUME = 0x00090018;
    public const uint FSCTL_UNLOCK_VOLUME = 0x0009001C;
    public const uint FSCTL_DISMOUNT_VOLUME = 0x00090020;

    public const ulong DISK_ATTRIBUTE_OFFLINE = 0x1;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr sec, uint disposition, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern unsafe bool DeviceIoControl(SafeFileHandle h, uint code, void* inBuf, uint inSize, void* outBuf, uint outSize, out uint returned, IntPtr overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern unsafe bool ReadFile(SafeFileHandle h, void* buffer, uint toRead, out uint read, IntPtr overlapped);

    public static SafeFileHandle Open(string path, uint access, uint flags = 0) =>
        CreateFile(path, access, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, flags, IntPtr.Zero);

    public static unsafe bool Ioctl(SafeFileHandle h, uint code) =>
        DeviceIoControl(h, code, null, 0, null, 0, out _, IntPtr.Zero);

    // ---------- Volumes ----------
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr FindFirstVolume(char[] name, int len);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool FindNextVolume(IntPtr h, char[] name, int len);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool FindVolumeClose(IntPtr h);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool GetVolumePathNamesForVolumeName(string volumeName, char[] buffer, int len, out int required);

    // ---------- Structures ----------
    [StructLayout(LayoutKind.Sequential)]
    public struct STORAGE_DEVICE_NUMBER { public uint DeviceType; public uint DeviceNumber; public uint PartitionNumber; }

    [StructLayout(LayoutKind.Sequential)]
    public struct STORAGE_PROPERTY_QUERY { public int PropertyId; public int QueryType; public int AdditionalParameters; }

    [StructLayout(LayoutKind.Sequential)]
    public struct GET_DISK_ATTRIBUTES { public uint Version; public uint Reserved1; public ulong Attributes; }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct SET_DISK_ATTRIBUTES
    {
        public uint Version;
        public byte Persist;
        public fixed byte Reserved1[3];
        public ulong Attributes;
        public ulong AttributesMask;
        public fixed uint Reserved2[4];
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct ATA_PASS_THROUGH_EX
    {
        public ushort Length;
        public ushort AtaFlags;
        public byte PathId, TargetId, Lun, ReservedAsUchar;
        public uint DataTransferLength;
        public uint TimeOutValue;
        public uint ReservedAsUlong;
        public UIntPtr DataBufferOffset;
        public fixed byte PreviousTaskFile[8];
        public fixed byte CurrentTaskFile[8];
    }

    public const ushort ATA_FLAGS_DRDY_REQUIRED = 0x01;

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct SCSI_PASS_THROUGH
    {
        public ushort Length;
        public byte ScsiStatus, PathId, TargetId, Lun, CdbLength, SenseInfoLength, DataIn;
        public uint DataTransferLength;
        public uint TimeOutValue;
        public UIntPtr DataBufferOffset;
        public uint SenseInfoOffset;
        public fixed byte Cdb[16];
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct SCSI_PASS_THROUGH_WITH_SENSE
    {
        public SCSI_PASS_THROUGH Spt;
        public fixed byte Sense[32];
    }

    public const byte SCSI_IOCTL_DATA_UNSPECIFIED = 2;

    // ---------- SetupAPI / CfgMgr ----------
    public static readonly Guid GUID_DEVINTERFACE_DISK = new("53f56307-b6bf-11d0-94f2-00a0c91efb8b");
    public static readonly Guid GUID_DEVCLASS_DISKDRIVE = new("4d36e967-e325-11ce-bfc1-08002be10318");

    public const int DIGCF_PRESENT = 0x02;
    public const int SPDRP_DEVICEDESC = 0x00;
    public const int SPDRP_FRIENDLYNAME = 0x0C;
    public const int ERROR_NO_MORE_ITEMS = 259;
    public const int DIF_PROPERTYCHANGE = 0x12;
    public const int DICS_ENABLE = 1, DICS_DISABLE = 2;
    public const int DICS_FLAG_GLOBAL = 1;
    public const int CM_GET_DEVICE_INTERFACE_LIST_PRESENT = 0;
    public const int CR_SUCCESS = 0;
    public const int CM_PROB_DISABLED = 22;
    public const uint DN_HAS_PROBLEM = 0x00000400;
    public const uint DN_STARTED = 0x00000008;

    [StructLayout(LayoutKind.Sequential)]
    public struct SP_DEVINFO_DATA { public int cbSize; public Guid ClassGuid; public int DevInst; public IntPtr Reserved; }

    [StructLayout(LayoutKind.Sequential)]
    public struct SP_PROPCHANGE_PARAMS
    {
        public int HeaderSize;        // SP_CLASSINSTALL_HEADER.cbSize
        public int InstallFunction;   // SP_CLASSINSTALL_HEADER.InstallFunction
        public int StateChange;
        public int Scope;
        public int HwProfile;
    }

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr SetupDiGetClassDevs(ref Guid classGuid, IntPtr enumerator, IntPtr hwndParent, int flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    public static extern bool SetupDiEnumDeviceInfo(IntPtr set, int index, ref SP_DEVINFO_DATA data);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool SetupDiGetDeviceInstanceId(IntPtr set, ref SP_DEVINFO_DATA data, char[] buffer, int size, out int required);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool SetupDiGetDeviceRegistryProperty(IntPtr set, ref SP_DEVINFO_DATA devInfo, int property, out int regType, byte[] buffer, int bufferSize, out int required);

    [DllImport("setupapi.dll", SetLastError = true)]
    public static extern bool SetupDiSetClassInstallParams(IntPtr set, ref SP_DEVINFO_DATA data, ref SP_PROPCHANGE_PARAMS p, int size);

    [DllImport("setupapi.dll", SetLastError = true)]
    public static extern bool SetupDiCallClassInstaller(int function, IntPtr set, ref SP_DEVINFO_DATA data);

    [DllImport("setupapi.dll", SetLastError = true)]
    public static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    public static extern int CM_Get_Device_Interface_List_Size(out int len, ref Guid guid, string deviceId, int flags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    public static extern int CM_Get_Device_Interface_List(ref Guid guid, string deviceId, char[] buffer, int len, int flags);

    [DllImport("cfgmgr32.dll")]
    public static extern int CM_Get_DevNode_Status(out uint status, out uint problem, int devInst, int flags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    public static extern int CM_Locate_DevNode(out int devInst, string deviceId, int flags);

    public const int CM_DISABLE_UI_NOT_OK = 0x4;
    public const int CR_REMOVE_VETOED = 0x17;

    /// <summary>Disables until the next Windows restart (no CM_DISABLE_PERSIST), refuses instead of scheduling a restart.</summary>
    [DllImport("cfgmgr32.dll")]
    public static extern int CM_Disable_DevNode(int devInst, int flags);

    [DllImport("cfgmgr32.dll")]
    public static extern int CM_Enable_DevNode(int devInst, int flags);
}
