using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Imrdy.Windows.Commands;

/// <summary>
/// Walks the process tree to find the terminal window PID for a Claude session.
/// Uses pure .NET Process APIs with P/Invoke for parent PID lookup.
/// Caches terminal PID per session to avoid repeated walks.
/// </summary>
internal static class ProcessResolver
{
    private static readonly Dictionary<string, int?> Cache = new();

    /// <summary>
    /// Resolves the terminal/console window PID for the given Claude process.
    /// Walks up the process tree looking for a process with a main window handle.
    /// Returns null if resolution fails.
    /// </summary>
    public static int? ResolveTerminalPid(int claudePid, string sessionId)
    {
        if (Cache.TryGetValue(sessionId, out var cached))
        {
            return cached;
        }

        var terminalPid = WalkToTerminal(claudePid);
        Cache[sessionId] = terminalPid;
        return terminalPid;
    }

    /// <summary>
    /// Clears the cached terminal PID for a session (e.g., on session end).
    /// </summary>
    public static void ClearSession(string sessionId)
    {
        Cache.Remove(sessionId);
    }

    /// <summary>
    /// Clears all cached terminal PIDs.
    /// </summary>
    public static void ClearAll()
    {
        Cache.Clear();
    }

    private static int? WalkToTerminal(int startPid)
    {
        try
        {
            var currentPid = startPid;
            var visited = new HashSet<int>();

            // Walk up the process tree (max 10 levels to prevent infinite loops)
            for (var i = 0; i < 10; i++)
            {
                if (!visited.Add(currentPid))
                {
                    break; // Cycle detected
                }

                Process process;
                try
                {
                    process = Process.GetProcessById(currentPid);
                }
                catch (ArgumentException)
                {
                    break; // Process no longer exists
                }

                using (process)
                {
                    // If this process has a main window, it's likely the terminal
                    if (process.MainWindowHandle != IntPtr.Zero)
                    {
                        return currentPid;
                    }

                    // Try to get parent PID
                    var parentPid = GetParentPid(currentPid);
                    if (parentPid is null || parentPid == currentPid)
                    {
                        break;
                    }

                    currentPid = parentPid.Value;
                }
            }
        }
        catch (Exception)
        {
            // Best-effort resolution — don't crash the hook
        }

        return null;
    }

    private static int? GetParentPid(int pid)
    {
        var handle = CreateToolhelp32Snapshot(0x00000002 /* TH32CS_SNAPPROCESS */, 0);
        if (handle == IntPtr.Zero || handle == new IntPtr(-1))
        {
            return null;
        }

        try
        {
            var entry = new PROCESSENTRY32 { dwSize = (uint)Marshal.SizeOf<PROCESSENTRY32>() };
            if (!Process32First(handle, ref entry))
            {
                return null;
            }

            do
            {
                if (entry.th32ProcessID == pid)
                {
                    return (int)entry.th32ParentProcessID;
                }
            } while (Process32Next(handle, ref entry));
        }
        finally
        {
            CloseHandle(handle);
        }

        return null;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool Process32First(IntPtr hSnapshot, ref PROCESSENTRY32 lppe);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool Process32Next(IntPtr hSnapshot, ref PROCESSENTRY32 lppe);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct PROCESSENTRY32
    {
        public uint dwSize;
        public uint cntUsage;
        public uint th32ProcessID;
        public IntPtr th32DefaultHeapID;
        public uint th32ModuleID;
        public uint cntThreads;
        public uint th32ParentProcessID;
        public int pcPriClassBase;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szExeFile;
    }
}
