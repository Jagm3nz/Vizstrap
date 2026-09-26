using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Vizstrap.Core.Platform;

public static class ProcessPaths
{
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const int ProcessCommandLineInformation = 60;

    /// <summary>
    /// Full image path of a process. Unlike <see cref="Process.MainModule"/> this only needs limited query
    /// rights, so it also works for Roblox, whose anti-tamper blocks reading its modules.
    /// </summary>
    public static string? TryGetImagePath(int processId)
    {
        var handle = OpenProcess(ProcessQueryLimitedInformation, false, processId);

        if (handle == IntPtr.Zero)
            return null;

        try
        {
            var buffer = new StringBuilder(32768);
            int size = buffer.Capacity;

            return QueryFullProcessImageNameW(handle, 0, buffer, ref size) ? buffer.ToString(0, size) : null;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    /// <summary>The command line a process was started with, read with the same limited rights (Windows 8.1+).</summary>
    public static string? TryGetCommandLine(int processId)
    {
        var handle = OpenProcess(ProcessQueryLimitedInformation, false, processId);

        if (handle == IntPtr.Zero)
            return null;

        try
        {
            NtQueryInformationProcess(handle, ProcessCommandLineInformation, IntPtr.Zero, 0, out int length);

            if (length <= 0)
                return null;

            var buffer = Marshal.AllocHGlobal(length);

            try
            {
                if (NtQueryInformationProcess(handle, ProcessCommandLineInformation, buffer, length, out _) != 0)
                    return null;

                // a UNICODE_STRING: byte length, then (aligned) a pointer to the text, which follows it in the buffer
                int bytes = (ushort)Marshal.ReadInt16(buffer);
                var text = Marshal.ReadIntPtr(buffer, IntPtr.Size);
                return Marshal.PtrToStringUni(text, bytes / 2);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    /// <summary>Running processes with the given name whose executable lives under <paramref name="directory"/>.</summary>
    public static IReadOnlyList<int> FindRunningUnder(string processName, string directory)
    {
        string root = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var found = new List<int>();

        foreach (var process in Process.GetProcessesByName(processName))
        {
            using (process)
            {
                string? path = TryGetImagePath(process.Id);

                if (path is not null && path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                    found.Add(process.Id);
            }
        }

        return found;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint desiredAccess, bool inheritHandle, int processId);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool QueryFullProcessImageNameW(IntPtr process, int flags, StringBuilder name, ref int size);

    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationProcess(IntPtr process, int informationClass, IntPtr buffer, int length, out int returnLength);
}
