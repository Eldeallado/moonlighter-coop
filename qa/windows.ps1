# Puts the test copies side by side: host on the left, guest on the right.
Add-Type -Namespace CoopQa -Name Win -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
[DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
'@ -ErrorAction SilentlyContinue

function Place-Window($proc, [int]$x, [int]$y, [int]$w = 816, [int]$h = 489) {
    for ($i = 0; $i -lt 60; $i++) {
        $proc.Refresh()
        if ($proc.MainWindowHandle -ne [IntPtr]::Zero) {
            [CoopQa.Win]::ShowWindow($proc.MainWindowHandle, 4) | Out-Null   # restore if maximized
            [CoopQa.Win]::SetWindowPos($proc.MainWindowHandle, [IntPtr]::Zero, $x, $y, $w, $h, 0x0050) | Out-Null
            return $true
        }
        Start-Sleep -Milliseconds 500
    }
    return $false
}

function Start-Copy($dir, $argsLine, [int]$x, [int]$y = 40, [int]$w = 816, [int]$h = 489) {
    $p = Start-Process "$dir\Moonlighter.exe" -ArgumentList $argsLine -WorkingDirectory $dir -PassThru
    Start-Job -ArgumentList $p.Id, $x, $y, $w, $h -ScriptBlock {
        param($procId, $x, $y, $w, $h)
        Add-Type -Namespace CoopQa -Name Win -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
[DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
'@
        Add-Type -Namespace CoopQa -Name Dlg -MemberDefinition @"
[DllImport("user32.dll")] public static extern IntPtr FindWindowEx(IntPtr p, IntPtr after, string cls, string title);
[DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
"@
        # The game resizes its window a few times while starting: keep placing it for a while.
        # If Unity shows its "Configuration" dialog (a key such as Alt was held at launch), press its Play button
        # without taking the focus (BM_CLICK straight to the button).
        for ($i = 0; $i -lt 40; $i++) {
            $q = Get-Process -Id $procId -ErrorAction SilentlyContinue
            if (-not $q) { break }
            if ($q.MainWindowTitle -like '*Configuration*') {
                $btn = [IntPtr]::Zero
                foreach ($txt in "Play!", "&Play!", "¡Jugar!", "Jugar") { if ($btn -eq [IntPtr]::Zero) { $btn = [CoopQa.Dlg]::FindWindowEx($q.MainWindowHandle, [IntPtr]::Zero, "Button", $txt) } }
                if ($btn -ne [IntPtr]::Zero) { [CoopQa.Dlg]::SendMessage($btn, 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null }
                Start-Sleep -Milliseconds 1500; continue
            }
            if ($q.MainWindowHandle -ne [IntPtr]::Zero) {
                [CoopQa.Win]::ShowWindow($q.MainWindowHandle, 4) | Out-Null
                [CoopQa.Win]::SetWindowPos($q.MainWindowHandle, [IntPtr]::Zero, $x, $y, $w, $h, 0x0050) | Out-Null
            }
            Start-Sleep -Milliseconds 1500
        }
    } | Out-Null
    return $p
}
