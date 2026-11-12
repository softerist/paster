using System;
using System.Runtime.InteropServices;

namespace Paster.Windows
{
    // User Interface Privilege Isolation (UIPI) discards simulated input sent to a window of a higher integrity level (a program running as
    // administrator) while SendInput still reports success, so Paster checks the destination first. Kept separate from WindowsPlatform so the
    // check can be exercised on its own.
    internal static class TargetIntegrity
    {
        public const int Low = 0x1000, Medium = 0x2000, High = 0x3000;
        private static int own;
        // A process Paster can open but whose token it cannot read is treated as higher: processes at Paster's own level always grant that.
        // Processes Paster cannot open at all (other users' processes, protected processes) are not judged, so they are never blocked by mistake.
        public static bool IsInputBlocked(IntPtr window)
        {
            uint pid; GetWindowThreadProcessId(window, out pid); if (pid == 0) return false;
            int self = OwnLevel(); if (self < 0) return false;
            IntPtr process = OpenProcess(0x1000, false, pid); if (process == IntPtr.Zero) return false;
            try { int level = Level(process); return level < 0 ? self < High : level > self; } finally { CloseHandle(process); }
        }
        public static int OwnLevel() { if (own == 0) own = Level(GetCurrentProcess()); return own; }
        // The mandatory label's RID (0x1000 low, 0x2000 medium, 0x3000 high, 0x4000 system), or -1 when the token cannot be read.
        public static int Level(IntPtr process)
        {
            IntPtr token; if (!OpenProcessToken(process, 8, out token)) return -1;
            try
            {
                int size; GetTokenInformation(token, 25, IntPtr.Zero, 0, out size); if (size <= 0) return -1; IntPtr buffer = Marshal.AllocHGlobal(size);
                try
                {
                    if (!GetTokenInformation(token, 25, buffer, size, out size)) return -1;
                    IntPtr sid = Marshal.ReadIntPtr(buffer); int count = Marshal.ReadByte(GetSidSubAuthorityCount(sid));
                    return count > 0 ? Marshal.ReadInt32(GetSidSubAuthority(sid, (uint)(count - 1))) : -1;
                }
                finally { Marshal.FreeHGlobal(buffer); }
            }
            finally { CloseHandle(token); }
        }
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
        [DllImport("kernel32.dll")] private static extern IntPtr OpenProcess(uint access, bool inherit, uint processId);
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
        [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentProcess();
        [DllImport("advapi32.dll")] private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
        [DllImport("advapi32.dll")] private static extern bool GetTokenInformation(IntPtr token, int infoClass, IntPtr info, int length, out int returnLength);
        [DllImport("advapi32.dll")] private static extern IntPtr GetSidSubAuthority(IntPtr sid, uint index);
        [DllImport("advapi32.dll")] private static extern IntPtr GetSidSubAuthorityCount(IntPtr sid);
    }
}
