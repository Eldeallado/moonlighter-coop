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
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr OpenDesktop(string name, int flags, bool inherit, uint access);
    [DllImport("user32.dll")] static extern bool SetThreadDesktop(IntPtr desk);
    delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] static extern bool EnumDesktopWindows(IntPtr desk, EnumProc f, IntPtr l);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, System.Text.StringBuilder s, int n);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindowEx(IntPtr p, IntPtr after, string cls, string title);
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
    /// Unity's "Configuration" dialog (it shows when Alt is held at launch): press its Play button on the hidden desktop.
    public static int ClickPlay()
    {
        int clicked = 0;
        var t = new System.Threading.Thread(() =>
        {
            var d = OpenDesktop("CoopQaOculto", 0, false, 0x10000000);
            if (d == IntPtr.Zero || !SetThreadDesktop(d)) return;
            EnumDesktopWindows(d, (h, l) =>
            {
                var sb = new System.Text.StringBuilder(256); GetWindowText(h, sb, 256);
                if (sb.ToString().Contains("Configuration"))
                {
                    foreach (var txt in new[] { "Play!", "&Play!" })
                    {
                        var b = FindWindowEx(h, IntPtr.Zero, "Button", txt);
                        if (b != IntPtr.Zero) { SendMessage(b, 0x00F5, IntPtr.Zero, IntPtr.Zero); clicked++; break; }
                    }
                }
                return true;
            }, IntPtr.Zero);
        });
        t.Start(); t.Join();
        return clicked;
    }
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

function Start-Oculto($dir, $argsLine) {
    $id = [CoopOculto]::Start("$dir\Moonlighter.exe", $argsLine, $dir)
    for ($i = 0; $i -lt 10; $i++) { Start-Sleep 1; if ([CoopOculto]::ClickPlay() -gt 0) { break } }
    $id
}
