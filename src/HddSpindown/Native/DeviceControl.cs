using System.ComponentModel;
using System.Runtime.InteropServices;
using static HddSpindown.Native.NativeMethods;

namespace HddSpindown.Native;

/// <summary>Enables/disables a drive in Device Manager (like right-click -> Disable device).</summary>
public static class DeviceControl
{
    public static void SetEnabled(string instanceId, bool enabled)
    {
        var classGuid = GUID_DEVCLASS_DISKDRIVE;
        IntPtr set = SetupDiGetClassDevs(ref classGuid, IntPtr.Zero, IntPtr.Zero, DIGCF_PRESENT);
        if (set == new IntPtr(-1)) throw new Win32Exception();
        try
        {
            for (int i = 0; ; i++)
            {
                var dev = new SP_DEVINFO_DATA { cbSize = Marshal.SizeOf<SP_DEVINFO_DATA>() };
                if (!SetupDiEnumDeviceInfo(set, i, ref dev)) break;

                var buf = new char[512];
                if (!SetupDiGetDeviceInstanceId(set, ref dev, buf, buf.Length, out int req)) continue;
                if (!string.Equals(new string(buf, 0, Math.Max(0, req - 1)), instanceId, StringComparison.OrdinalIgnoreCase)) continue;

                var p = new SP_PROPCHANGE_PARAMS
                {
                    HeaderSize = 8,
                    InstallFunction = DIF_PROPERTYCHANGE,
                    StateChange = enabled ? DICS_ENABLE : DICS_DISABLE,
                    Scope = DICS_FLAG_GLOBAL,
                    HwProfile = 0,
                };
                if (!SetupDiSetClassInstallParams(set, ref dev, ref p, Marshal.SizeOf<SP_PROPCHANGE_PARAMS>())
                    || !SetupDiCallClassInstaller(DIF_PROPERTYCHANGE, set, ref dev))
                    throw new Win32Exception(Marshal.GetLastWin32Error(),
                        enabled ? Loc.T("Laufwerk konnte nicht aktiviert werden", "Could not enable drive") : Loc.T("Laufwerk konnte nicht deaktiviert werden", "Could not disable drive"));
                return;
            }
            throw new InvalidOperationException(Loc.T("Laufwerk nicht gefunden: ", "Drive not found: ") + instanceId);
        }
        finally { SetupDiDestroyDeviceInfoList(set); }
    }
}
