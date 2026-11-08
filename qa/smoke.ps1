# Quick check before a release: two copies, join by Steam code, both reach the same dungeon room.
$q = $PSScriptRoot
. "$q\windows.ps1"
Get-Process Moonlighter -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep 2
foreach ($dd in "test-host", "test-guest") { Get-ChildItem "E:\CoopAnything\$dd\BepInEx" -Filter "coop_*" -File | ForEach-Object { $_.Delete() } }
$common = "-screen-fullscreen 0 -screen-width 640 -screen-height 360 -coopwin 640x360 -coopwindowed -coopdrive"
Start-Copy "E:\CoopAnything\test-host" "$common -coopbot -coopscenario none" -1920 0 656 399 | Out-Null
Start-Sleep 8
Start-Copy "E:\CoopAnything\test-guest" "$common -coopdriveport 7801" -1280 0 656 399 | Out-Null
Start-Sleep 45
& "$q\drive.ps1" host "coop create" | Out-Null
Start-Sleep 4
$st = & "$q\drive.ps1" host "coop status"
$code = [regex]::Match($st, 'code=(\w+)').Groups[1].Value
"code: $code"
& "$q\drive.ps1" host "coop listen" | Out-Null
Start-Sleep 1
& "$q\drive.ps1" guest "coop ip 127.0.0.1" | Out-Null
Start-Sleep 30
"guest: " + (& "$q\drive.ps1" guest "coop status")
foreach ($w in "host", "guest") { & "$q\drive.ps1" $w "inv on" | Out-Null }
& "$q\drive.ps1" host "dungeon Golem" | Out-Null
Start-Sleep 30
foreach ($w in "host", "guest") { $i = & "$q\drive.ps1" $w info; "$w " + [regex]::Match($i, 'zone=\S+').Value }
