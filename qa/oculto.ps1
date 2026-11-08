# Starts test copies on a separate, invisible Windows desktop ("CoopQaOculto"): they never show up on the
# player's screen and can never take the mouse, keyboard or focus. Drive them with drive.ps1 and look at them
# with the driver's "shot" command (the game saves its own screenshot).
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class CoopOculto
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct STARTUPINFO { public int cb; public string lpReserved; public string lpDesktop; public string lpTitle;
        public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
        public short wShowWindow, cbReserved2; public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError; }
    [StructLayout(LayoutKind.Sequential)]
    struct PROCESS_INFORMATION { public IntPtr hProcess, hThread; public int dwProcessId, dwThreadId; }
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern IntPtr CreateDesktop(string name, IntPtr dev, IntPtr mode, int flags, uint access, IntPtr sa);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool CreateProcess(string app, string cmd, IntPtr pa, IntPtr ta, bool inherit, int flags,
        IntPtr env, string dir, ref STARTUPINFO si, out PROCESS_INFORMATION pi);
    static IntPtr _desk;
    public static int Start(string exe, string args, string dir)
    {
        if (_desk == IntPtr.Zero) _desk = CreateDesktop("CoopQaOculto", IntPtr.Zero, IntPtr.Zero, 0, 0x10000000, IntPtr.Zero);
        if (_desk == IntPtr.Zero) throw new Exception("CreateDesktop " + Marshal.GetLastWin32Error());
        var si = new STARTUPINFO(); si.cb = Marshal.SizeOf(si); si.lpDesktop = "CoopQaOculto";
        PROCESS_INFORMATION pi;
        if (!CreateProcess(exe, "\"" + exe + "\" " + args, IntPtr.Zero, IntPtr.Zero, false, 0, IntPtr.Zero, dir, ref si, out pi))
            throw new Exception("CreateProcess " + Marshal.GetLastWin32Error());
        return pi.dwProcessId;
    }
}
'@ -ErrorAction SilentlyContinue

function Start-Oculto($dir, $argsLine) { [CoopOculto]::Start("$dir\Moonlighter.exe", $argsLine, $dir) }
