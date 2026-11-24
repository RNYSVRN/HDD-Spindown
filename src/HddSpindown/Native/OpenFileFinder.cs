using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace HddSpindown.Native;

/// <summary>A program that holds files or folders open on a drive.</summary>
public sealed record BlockingProcess(int Pid, string ProcessName, string DisplayName, string? ExamplePath)
{
    /// <summary>
    /// Explorer watches every drive and usually releases its access by itself when locking,
    /// so it is rarely the actual cause. It is therefore sorted to the end and marked.
    /// </summary>
    public bool UsuallyReleases => ProcessName.Equals("explorer.exe", StringComparison.OrdinalIgnoreCase);

    public override string ToString()
    {
        string text = string.Equals(DisplayName, ProcessName, StringComparison.OrdinalIgnoreCase) ? ProcessName : $"{DisplayName} ({ProcessName})";
        return UsuallyReleases ? text + Loc.T(", meist unproblematisch, gibt den Zugriff selbst frei", ", usually harmless, releases access by itself") : text;
    }
}

/// <summary>
/// Finds processes that hold files/folders open on given volumes (e.g. Explorer windows,
/// media players, virus scanners). Works like Sysinternals "Handle": list all file handles in the system,
/// duplicate them into our own process and resolve the path. Requires admin rights.
/// Read-only: nothing is closed or modified.
/// </summary>
public static class OpenFileFinder
{
    /// <param name="volumes">Volumes of the drive (\\?\Volume{GUID}\ and drive letters).</param>
    public static List<BlockingProcess> Find(IReadOnlyList<VolumeInfo> volumes, TimeSpan timeout)
    {
        var prefixes = volumes.Select(v => v.VolumeName.TrimEnd('\\') + "\\").ToList();
        var letters = volumes.SelectMany(v => v.MountPoints).Where(m => m.Length >= 2).ToList();
        if (prefixes.Count == 0) return [];

        var found = new Dictionary<int, string?>();
        var cts = new CancellationTokenSource(timeout);

        // 1) Processes whose executable resides on the drive
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                string? image = ImagePath(p.Id);
                if (image is not null && letters.Any(l => image.StartsWith(l, StringComparison.OrdinalIgnoreCase)))
                    found.TryAdd(p.Id, image);
            }
            catch { }
            finally { p.Dispose(); }
        }

        // 2) Open file handles (on a separate thread so a hanging call doesn't block the app)
        var scan = Task.Run(() => ScanHandles(prefixes, found, cts.Token));
        try { scan.Wait(timeout); } catch { }

        var result = new List<BlockingProcess>();
        int self = Environment.ProcessId;
        lock (found)
        {
            foreach (var (pid, path) in found)
            {
                if (pid == self) continue;
                result.Add(Describe(pid, path));
            }
        }
        return result.OrderBy(r => r.UsuallyReleases).ThenBy(r => r.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    private static void ScanHandles(List<string> prefixes, Dictionary<int, string?> found, CancellationToken ct)
    {
        var handles = QueryHandles();
        if (handles.Count == 0) return;

        ushort fileType = FileObjectTypeIndex(handles);
        if (fileType == 0) return;

        int self = Environment.ProcessId;
        var processHandles = new Dictionary<int, IntPtr>();
        var sb = new StringBuilder(1024);
        try
        {
            foreach (var h in handles)
            {
                if (ct.IsCancellationRequested) return;
                if (h.ObjectTypeIndex != fileType) continue;
                int pid = (int)h.UniqueProcessId;
                if (pid == self || pid <= 4) continue;
                lock (found) if (found.ContainsKey(pid)) continue;
                // Skip access masks for which queries on (synchronous) pipes can hang
                if (h.GrantedAccess is 0x0012019F or 0x001A019F or 0x00120189 or 0x00100000 or 0x0012008D) continue;

                if (!processHandles.TryGetValue(pid, out var proc))
                {
                    proc = OpenProcess(PROCESS_DUP_HANDLE, false, pid);
                    processHandles[pid] = proc;
                }
                if (proc == IntPtr.Zero) continue;

                if (!DuplicateHandle(proc, (IntPtr)h.HandleValue, GetCurrentProcess(), out var dup, 0, false, DUPLICATE_SAME_ACCESS))
                    continue;
                try
                {
                    if (GetFileType(dup) != FILE_TYPE_DISK) continue;
                    sb.Clear();
                    uint len = GetFinalPathNameByHandle(dup, sb, (uint)sb.Capacity, VOLUME_NAME_GUID);
                    if (len == 0 || len >= sb.Capacity) continue;
                    string path = sb.ToString();
                    if (prefixes.Any(pre => path.StartsWith(pre, StringComparison.OrdinalIgnoreCase)))
                        lock (found) found.TryAdd(pid, path);
                }
                finally { CloseHandle(dup); }
            }
        }
        finally
        {
            foreach (var p in processHandles.Values) if (p != IntPtr.Zero) CloseHandle(p);
        }
    }

    /// <summary>Type index of "File" objects: determined via our own freshly opened file handle.</summary>
    private static ushort FileObjectTypeIndex(List<HandleEntry> handles)
    {
        string probe = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "HddSpindown.dll");
        using var fs = new FileStream(probe, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var ours = (nuint)fs.SafeFileHandle.DangerousGetHandle();
        int self = Environment.ProcessId;
        // The list predates the open call, so query again
        foreach (var h in QueryHandles())
            if ((int)h.UniqueProcessId == self && h.HandleValue == ours) return h.ObjectTypeIndex;
        return 0;
    }

    private static BlockingProcess Describe(int pid, string? path)
    {
        string name = "?", display = "?";
        try
        {
            using var p = Process.GetProcessById(pid);
            name = p.ProcessName + ".exe";
            display = p.ProcessName;
            string? image = ImagePath(pid);
            if (image is not null)
            {
                name = Path.GetFileName(image);
                var desc = FileVersionInfo.GetVersionInfo(image).FileDescription;
                if (!string.IsNullOrWhiteSpace(desc)) display = desc.Trim();
            }
        }
        catch { }
        return new BlockingProcess(pid, name, display, path);
    }

    private static string? ImagePath(int pid)
    {
        IntPtr h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (h == IntPtr.Zero) return null;
        try
        {
            var sb = new StringBuilder(1024);
            int size = sb.Capacity;
            return QueryFullProcessImageName(h, 0, sb, ref size) ? sb.ToString() : null;
        }
        finally { CloseHandle(h); }
    }

    // ---------- Handle table ----------

    private readonly record struct HandleEntry(nuint UniqueProcessId, nuint HandleValue, uint GrantedAccess, ushort ObjectTypeIndex);

    private static unsafe List<HandleEntry> QueryHandles()
    {
        int size = 1 << 20;
        IntPtr buf = IntPtr.Zero;
        try
        {
            while (true)
            {
                buf = Marshal.AllocHGlobal(size);
                int status = NtQuerySystemInformation(SystemExtendedHandleInformation, buf, size, out int needed);
                if (status == STATUS_INFO_LENGTH_MISMATCH)
                {
                    Marshal.FreeHGlobal(buf); buf = IntPtr.Zero;
                    size = Math.Max(size * 2, needed + (1 << 16));
                    if (size > (512 << 20)) return [];
                    continue;
                }
                if (status != 0) return [];
                break;
            }

            long count = (long)*(nuint*)buf;
            var entries = (SYSTEM_HANDLE_TABLE_ENTRY_INFO_EX*)(buf + 2 * IntPtr.Size);
            var list = new List<HandleEntry>((int)Math.Min(count, int.MaxValue));
            for (long i = 0; i < count; i++)
            {
                var e = entries[i];
                list.Add(new HandleEntry(e.UniqueProcessId, e.HandleValue, e.GrantedAccess, e.ObjectTypeIndex));
            }
            return list;
        }
        finally { if (buf != IntPtr.Zero) Marshal.FreeHGlobal(buf); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SYSTEM_HANDLE_TABLE_ENTRY_INFO_EX
    {
        public IntPtr Object;
        public nuint UniqueProcessId;
        public nuint HandleValue;
        public uint GrantedAccess;
        public ushort CreatorBackTraceIndex;
        public ushort ObjectTypeIndex;
        public uint HandleAttributes;
        public uint Reserved;
    }

    private const int SystemExtendedHandleInformation = 64;
    private const int STATUS_INFO_LENGTH_MISMATCH = unchecked((int)0xC0000004);
    private const uint PROCESS_DUP_HANDLE = 0x0040, PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    private const uint DUPLICATE_SAME_ACCESS = 0x2;
    private const uint FILE_TYPE_DISK = 0x1;
    private const uint VOLUME_NAME_GUID = 0x1;

    [DllImport("ntdll.dll")]
    private static extern int NtQuerySystemInformation(int infoClass, IntPtr info, int length, out int returnLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DuplicateHandle(IntPtr sourceProcess, IntPtr sourceHandle, IntPtr targetProcess,
        out IntPtr targetHandle, uint access, bool inherit, uint options);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr h);

    [DllImport("kernel32.dll")]
    private static extern uint GetFileType(IntPtr h);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "GetFinalPathNameByHandleW")]
    private static extern uint GetFinalPathNameByHandle(IntPtr h, StringBuilder path, uint length, uint flags);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "QueryFullProcessImageNameW")]
    private static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder name, ref int size);
}

/// <summary>Locking failed because programs hold files open on the drive.</summary>
public sealed class DiskInUseException : Exception
{
    public DiskInUseException(string message, IReadOnlyList<BlockingProcess> processes, Exception inner)
        : base(message, inner) => Processes = processes;

    public IReadOnlyList<BlockingProcess> Processes { get; }
}
