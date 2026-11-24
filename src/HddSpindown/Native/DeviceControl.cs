using System.ComponentModel;
using System.Runtime.InteropServices;
using static HddSpindown.Native.NativeMethods;

namespace HddSpindown.Native;

/// <summary>
/// Enables/disables a drive (like Device Manager). Uses the Configuration Manager directly:
/// it either really stops the device right away or refuses with a reason. The Device Manager
/// install function (SetupDi... DIF_PROPERTYCHANGE) can instead report success while it only
/// schedules the change for the next restart, which left drives reachable although they
/// counted as disabled.
/// </summary>
public static class DeviceControl
{
    public static void Disable(string instanceId)
    {
        int devInst = Locate(instanceId);
        int cr = CM_Disable_DevNode(devInst, CM_DISABLE_UI_NOT_OK);
        if (cr != CR_SUCCESS)
            throw new InvalidOperationException(cr == CR_REMOVE_VETOED
                ? Loc.T("Windows lässt das Deaktivieren nicht zu, weil die Platte noch verwendet wird.", "Windows does not allow disabling because the drive is still in use.")
                : Loc.T($"Laufwerk konnte nicht deaktiviert werden (Code 0x{cr:X2}).", $"Could not disable drive (code 0x{cr:X2})."));
        if (!WaitFor(() => IsReallyDisabled(instanceId), TimeSpan.FromSeconds(10)))
            throw new InvalidOperationException(Loc.T("Windows hat das Laufwerk nicht wirklich deaktiviert.", "Windows did not really disable the drive."));
    }

    public static void Enable(string instanceId)
    {
        int devInst = Locate(instanceId);
        int cr = CM_Enable_DevNode(devInst, 0);
        if (cr == CR_SUCCESS && WaitFor(() => IsStarted(instanceId), TimeSpan.FromSeconds(20))) return;

        // Disabled permanently elsewhere (e.g. Device Manager or an older version): enable via the install function
        SetEnabledViaSetupApi(instanceId, true);
        if (!WaitFor(() => IsStarted(instanceId), TimeSpan.FromSeconds(20)))
            throw new InvalidOperationException(Loc.T("Windows hat das Laufwerk nicht wieder gestartet.", "Windows did not start the drive again."));
    }

    /// <summary>true = device is stopped and marked as disabled (no driver loaded, unreachable for everyone).</summary>
    public static bool IsReallyDisabled(string instanceId) =>
        CM_Locate_DevNode(out int devInst, instanceId, 0) == CR_SUCCESS
        && CM_Get_DevNode_Status(out uint status, out uint problem, devInst, 0) == CR_SUCCESS
        && (status & DN_STARTED) == 0 && (status & DN_HAS_PROBLEM) != 0 && problem == CM_PROB_DISABLED;

    private static bool IsStarted(string instanceId) =>
        CM_Locate_DevNode(out int devInst, instanceId, 0) == CR_SUCCESS
        && CM_Get_DevNode_Status(out uint status, out _, devInst, 0) == CR_SUCCESS
        && (status & DN_STARTED) != 0;

    private static int Locate(string instanceId) =>
        CM_Locate_DevNode(out int devInst, instanceId, 0) == CR_SUCCESS
            ? devInst
            : throw new InvalidOperationException(Loc.T("Laufwerk nicht gefunden: ", "Drive not found: ") + instanceId);

    private static bool WaitFor(Func<bool> condition, TimeSpan timeout)
    {
        var until = DateTime.UtcNow + timeout;
        while (true)
        {
            if (condition()) return true;
            if (DateTime.UtcNow > until) return false;
            Thread.Sleep(200);
        }
    }

    private static void SetEnabledViaSetupApi(string instanceId, bool enabled)
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
