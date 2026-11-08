param([int]$Parte = 0)
# Two test copies on the hidden desktop, x4 speed, a brand new game (nothing is saved): both play the tutorial.
$q = $PSScriptRoot
. "$q\oculto.ps1"
foreach ($dd in "test-host", "test-guest") { Get-ChildItem "E:\CoopAnything\$dd\BepInEx" -Filter "coop_*" -File | ForEach-Object { $_.Delete() } }
$common = "-screen-fullscreen 0 -screen-width 640 -screen-height 360 -coopwin 640x360 -coopwindowed -coopdrive -coopspeed 4"
"host pid " + (Start-Oculto "E:\CoopAnything\test-host" "$common -coop host")
Start-Sleep 25
"newgame: " + (& "$q\drive.ps1" host "newgame $Parte")
Start-Sleep 5
"guest pid " + (Start-Oculto "E:\CoopAnything\test-guest" "$common -coop join 127.0.0.1")
Start-Sleep 50
foreach ($w in "host", "guest") { "$w " + (& "$q\drive.ps1" $w "tutorial") }
