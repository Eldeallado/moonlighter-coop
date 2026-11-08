param([int]$Players = 5, [int]$GuestDelay = 15)
. "$PSScriptRoot\windows.ps1"
# Opens host + (Players-1) guests for remote driving, 640x360 each in a 3x2 grid on the left monitor.
$ws = "E:\CoopAnything"
$dirs = @("test-host", "test-guest", "test-guest2", "test-guest3", "test-guest4")[0..($Players - 1)]
Get-Process Moonlighter -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep 2
foreach ($d in $dirs) { Get-ChildItem "$ws\$d\BepInEx" -Filter "coop_*" -ErrorAction SilentlyContinue | ForEach-Object { $_.Delete() } }
$common = "-screen-fullscreen 0 -screen-width 640 -screen-height 360 -coopwin 640x360 -coopbot -coopscenario none -coopdrive"
for ($i = 0; $i -lt $dirs.Count; $i++) {
    $x = -1920 + ($i % 3) * 640; $y = if ($i -lt 3) { 0 } else { 400 }
    $role = if ($i -eq 0) { "-coop host" } else { "-coop join 127.0.0.1 -coopdriveport $(7800 + $i)" }
    Start-Copy "$ws\$($dirs[$i])" "$common $role" $x $y 656 399 | Out-Null
    Start-Sleep $(if ($i -eq 0) { $GuestDelay } else { 5 })
}
Start-Sleep 60
