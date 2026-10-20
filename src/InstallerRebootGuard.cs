using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace ServiceTool
{
    internal static class InstallerRebootGuard
    {
        [StructLayout(LayoutKind.Sequential)] private struct Luid { public uint Low; public int High; }
        [StructLayout(LayoutKind.Sequential)] private struct Privilege { public uint Count; public Luid Id; public uint Attributes; }
        [DllImport("advapi32.dll", SetLastError = true)] private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool LookupPrivilegeValue(string system, string name, out Luid value);
        [DllImport("advapi32.dll", SetLastError = true)] private static extern bool AdjustTokenPrivileges(IntPtr token, bool disableAll, ref Privilege privileges, uint length, IntPtr previous, IntPtr returned);
        [DllImport("advapi32.dll", SetLastError = true)] private static extern bool GetTokenInformation(IntPtr token, int information, IntPtr buffer, int length, out int required);
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
        internal static void Apply(IntPtr child)
        {
            // Only the suspended installer's token changes. The manager's own token is untouched.
            IntPtr token;
            if (!OpenProcessToken(child, 0x20 | 0x8, out token)) throw new Win32Exception(Marshal.GetLastWin32Error(), "Не удалось отключить право перезагрузки у установщика");
            try
            {
                foreach (string name in new[] { "SeShutdownPrivilege", "SeRemoteShutdownPrivilege" })
                {
                    Luid id; if (!LookupPrivilegeValue(null, name, out id)) throw new Win32Exception(Marshal.GetLastWin32Error());
                    var privilege = new Privilege { Count = 1, Id = id, Attributes = 4 }; // SE_PRIVILEGE_REMOVED
                    if (!AdjustTokenPrivileges(token, false, ref privilege, 0, IntPtr.Zero, IntPtr.Zero)) throw new Win32Exception(Marshal.GetLastWin32Error());
                    int required; GetTokenInformation(token, 3, IntPtr.Zero, 0, out required);
                    IntPtr buffer = Marshal.AllocHGlobal(required);
                    try
                    {
                        if (!GetTokenInformation(token, 3, buffer, required, out required)) throw new Win32Exception(Marshal.GetLastWin32Error());
                        int count = Marshal.ReadInt32(buffer);
                        for (int i = 0; i < count; i++)
                            if (unchecked((uint)Marshal.ReadInt32(buffer, 4 + 12 * i)) == id.Low && Marshal.ReadInt32(buffer, 8 + 12 * i) == id.High)
                                throw new InvalidOperationException("Право перезагрузки осталось у установщика; запуск остановлен");
                    }
                    finally { Marshal.FreeHGlobal(buffer); }
                }
            }
            finally { CloseHandle(token); }
        }
    }
}
