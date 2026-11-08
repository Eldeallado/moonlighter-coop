param([string]$Out = "$env:TEMP\coop_shots", [string]$Tag = "")
# Screenshots of every Moonlighter window (host and guest), wherever they are on the desktop.
Add-Type -AssemblyName System.Drawing
Add-Type -Namespace CoopQa -Name Shot -MemberDefinition @'
[StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
[DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
[DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
'@ -ErrorAction SilentlyContinue
[CoopQa.Shot]::SetProcessDPIAware() | Out-Null
New-Item -ItemType Directory -Force $Out | Out-Null
$files = @()
foreach ($p in Get-Process Moonlighter -ErrorAction SilentlyContinue) {
    if ($p.MainWindowHandle -eq [IntPtr]::Zero) { continue }
    $r = New-Object CoopQa.Shot+RECT
    [CoopQa.Shot]::GetWindowRect($p.MainWindowHandle, [ref]$r) | Out-Null
    $w = $r.Right - $r.Left; $h = $r.Bottom - $r.Top
    if ($w -le 0 -or $h -le 0) { continue }
    $role = if ($p.Path -match "test-guest(\d)") { "invitado" + $Matches[1] } elseif ($p.Path -like "*test-guest*") { "invitado1" } else { "anfitrion" }
    $bmp = New-Object System.Drawing.Bitmap $w, $h
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($r.Left, $r.Top, 0, 0, $bmp.Size)
    $f = Join-Path $Out ("{0}{1}.png" -f $role, $Tag)
    $bmp.Save($f, [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose()
    $files += $f
}
$files
