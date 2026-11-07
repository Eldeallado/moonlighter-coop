param([int]$GuestDelay = 15)
. "$PSScriptRoot\windows.ps1"
# Opens both QA copies for remote driving (no scripted bot): drive them with drive.ps1.
$ws = "E:\CoopAnything"
Get-Process Moonlighter -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep 2
Get-ChildItem "$ws\test-host\BepInEx", "$ws\test-guest\BepInEx" -Filter "coop_*" -ErrorAction SilentlyContinue | ForEach-Object { $_.Delete() }
$common = "-screen-fullscreen 0 -screen-width 800 -screen-height 450 -coopbot -coopscenario none -coopdrive"
$h = Start-Copy "$ws\test-host" "$common -coop host" -1920
Start-Sleep $GuestDelay
$g = Start-Copy "$ws\test-guest" "$common -coop join 127.0.0.1" -1100
Start-Sleep 60
