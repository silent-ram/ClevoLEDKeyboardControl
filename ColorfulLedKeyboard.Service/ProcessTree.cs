using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ColorfulLedKeyboard.Service;

internal static class ProcessTree
{
    private const uint SnapshotProcesses = 0x00000002;
    private static readonly IntPtr InvalidHandle = new(-1);

    public static HashSet<int> Expand(IEnumerable<int> roots)
    {
        var result = roots.Where(pid => pid > 0).ToHashSet();
        var parents = SnapshotParents();
        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var pair in parents)
                if (result.Contains(pair.Value) && result.Add(pair.Key)) changed = true;
        }
        return result;
    }

    public static List<int> FindRoots(string processName, string executablePath)
    {
        var result = new List<int>();
        foreach (var process in Process.GetProcessesByName(processName))
        using (process)
        {
            if (!string.IsNullOrWhiteSpace(executablePath))
            {
                try { if (!string.Equals(process.MainModule?.FileName, executablePath, StringComparison.OrdinalIgnoreCase)) continue; }
                catch { continue; }
            }
            result.Add(process.Id);
        }
        return result;
    }

    private static Dictionary<int, int> SnapshotParents()
    {
        var result = new Dictionary<int, int>();
        var snapshot = CreateToolhelp32Snapshot(SnapshotProcesses, 0);
        if (snapshot == InvalidHandle) return result;
        try
        {
            var entry = new ProcessEntry32 { Size = (uint)Marshal.SizeOf<ProcessEntry32>() };
            if (!Process32First(snapshot, ref entry)) return result;
            do { result[(int)entry.ProcessId] = (int)entry.ParentProcessId; }
            while (Process32Next(snapshot, ref entry));
        }
        finally { CloseHandle(snapshot); }
        return result;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessEntry32
    {
        public uint Size, Usage, ProcessId;
        public IntPtr DefaultHeapId;
        public uint ModuleId, Threads, ParentProcessId;
        public int BasePriority;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string ExeFile;
    }

    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint processId);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool Process32First(IntPtr snapshot, ref ProcessEntry32 entry);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool Process32Next(IntPtr snapshot, ref ProcessEntry32 entry);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
}
