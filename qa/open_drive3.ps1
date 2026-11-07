param([int]$GuestDelay = 15)
. "$PSScriptRoot\windows.ps1"
# Opens host + 2 guests for remote driving: host and guest 1 on top, guest 2 below (left monitor).
$ws = "E:\CoopAnything"
Get-Process Moonlighter -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep 2
foreach ($d in "test-host", "test-guest", "test-guest2") { Get-ChildItem "$ws\$d\BepInEx" -Filter "coop_*" -ErrorAction SilentlyContinue | ForEach-Object { $_.Delete() } }
$common = "-screen-fullscreen 0 -screen-width 800 -screen-height 450 -coopbot -coopscenario none -coopdrive"
$h = Start-Copy "$ws\test-host" "$common -coop host" -1920 40
Start-Sleep $GuestDelay
$g = Start-Copy "$ws\test-guest" "$common -coop join 127.0.0.1" -1100 40
Start-Sleep 5
$g2 = Start-Copy "$ws\test-guest2" "$common -coop join 127.0.0.1 -coopdriveport 7802" -1920 540
Start-Sleep 60
