# Puts the test copies side by side: host on the left, guest on the right.
Add-Type -Namespace CoopQa -Name Win -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
[DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
'@ -ErrorAction SilentlyContinue

function Place-Window($proc, [int]$x, [int]$y, [int]$w = 816, [int]$h = 489) {
    for ($i = 0; $i -lt 60; $i++) {
        $proc.Refresh()
        if ($proc.MainWindowHandle -ne [IntPtr]::Zero) {
            [CoopQa.Win]::ShowWindow($proc.MainWindowHandle, 9) | Out-Null   # restore if maximized
            [CoopQa.Win]::SetWindowPos($proc.MainWindowHandle, [IntPtr]::Zero, $x, $y, $w, $h, 0x0040) | Out-Null
            return $true
        }
        Start-Sleep -Milliseconds 500
    }
    return $false
}

function Start-Copy($dir, $argsLine, [int]$x, [int]$y = 40) {
    $p = Start-Process "$dir\Moonlighter.exe" -ArgumentList $argsLine -WorkingDirectory $dir -PassThru
    Start-Job -ArgumentList $p.Id, $x, $y -ScriptBlock {
        param($procId, $x, $y)
        Add-Type -Namespace CoopQa -Name Win -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
[DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
'@
        # The game resizes its window a few times while starting: keep placing it for a while.
        for ($i = 0; $i -lt 40; $i++) {
            $q = Get-Process -Id $procId -ErrorAction SilentlyContinue
            if (-not $q) { break }
            if ($q.MainWindowHandle -ne [IntPtr]::Zero) {
                [CoopQa.Win]::ShowWindow($q.MainWindowHandle, 9) | Out-Null
                [CoopQa.Win]::SetWindowPos($q.MainWindowHandle, [IntPtr]::Zero, $x, $y, 816, 489, 0x0040) | Out-Null
            }
            Start-Sleep -Milliseconds 1500
        }
    } | Out-Null
    return $p
}
