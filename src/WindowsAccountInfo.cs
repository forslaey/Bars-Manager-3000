using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace ServiceTool
{
    internal static class WindowsAccountInfo
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct UserInfo1
        {
            [MarshalAs(UnmanagedType.LPWStr)] public string Name;
            public IntPtr Password;
            public uint PasswordAge, Privilege;
            public IntPtr HomeDirectory, Comment;
            public uint Flags;
            public IntPtr ScriptPath;
        }

        [DllImport("Netapi32.dll", CharSet = CharSet.Unicode)]
        private static extern int NetUserEnum(string server, int level, int filter, out IntPtr buffer,
            int preferredLength, out uint read, out uint total, ref IntPtr resume);
        [DllImport("Netapi32.dll")]
        private static extern int NetApiBufferFree(IntPtr buffer);

        public static string CurrentUserName()
        {
            using (WindowsIdentity identity = WindowsIdentity.GetCurrent()) return identity.Name;
        }

        // Enabled normal accounts on this computer, plus the current identity (which may be a domain account).
        public static List<string> ListLocalUsers()
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            IntPtr resume = IntPtr.Zero;
            int status;
            do
            {
                IntPtr buffer; uint read, total;
                status = NetUserEnum(null, 1, 2, out buffer, -1, out read, out total, ref resume);
                try
                {
                    if (status != 0 && status != 234) throw new Win32Exception(status, "Не удалось получить список пользователей Windows.");
                    int size = Marshal.SizeOf(typeof(UserInfo1));
                    for (uint i = 0; i < read; i++)
                    {
                        var user = (UserInfo1)Marshal.PtrToStructure(new IntPtr(buffer.ToInt64() + i * size), typeof(UserInfo1));
                        if ((user.Flags & 2) == 0 && !String.IsNullOrWhiteSpace(user.Name))
                            names.Add(Environment.MachineName + "\\" + user.Name);
                    }
                }
                finally { if (buffer != IntPtr.Zero) NetApiBufferFree(buffer); }
            } while (status == 234);
            string current = CurrentUserName();
            if (!String.IsNullOrWhiteSpace(current)) names.Add(current);
            var result = new List<string>(names); result.Sort(StringComparer.CurrentCultureIgnoreCase);
            return result;
        }

        public static SecurityIdentifier ValidateUserName(string name)
        {
            if (String.IsNullOrWhiteSpace(name)) throw new ArgumentException("Выбери пользователя Windows из списка.");
            try { return (SecurityIdentifier)new NTAccount(name).Translate(typeof(SecurityIdentifier)); }
            catch (IdentityNotMappedException) { throw new ArgumentException("Windows не находит выбранную учётную запись. Обнови список пользователей."); }
        }
    }
}
