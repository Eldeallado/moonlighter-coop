# Two test copies on a brand new game (in memory only, nothing is saved) to play the tutorial together.
$q = $PSScriptRoot
. "$q\windows.ps1"
foreach ($dd in "test-host", "test-guest") { Get-ChildItem "E:\CoopAnything\$dd\BepInEx" -Filter "coop_*" -File | ForEach-Object { $_.Delete() } }
$common = "-screen-fullscreen 0 -screen-width 640 -screen-height 360 -coopwin 640x360 -coopwindowed -coopdrive"
Start-Copy "E:\CoopAnything\test-host" "$common -coop host" -1920 0 656 399 | Out-Null
Start-Sleep 8
Start-Copy "E:\CoopAnything\test-guest" "$common -coop join 127.0.0.1" -1260 0 656 399 | Out-Null
Start-Sleep 45
"newgame: " + (& "$q\drive.ps1" host "newgame")
Start-Sleep 50
foreach ($w in "host", "guest") { "$w " + (& "$q\drive.ps1" $w "tutorial") }
