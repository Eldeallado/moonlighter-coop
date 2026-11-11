# Código universal: el anfitrión crea la partida (con Steam) y el invitado entra con el código «IP…», no con Steam.
param([string]$Out = "$env:TEMP\coop_codigo")
$q = $PSScriptRoot
. "$q\oculto.ps1"
New-Item -ItemType Directory -Force $Out | Out-Null
Get-Process Moonlighter -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep 2
foreach ($dd in "test-host", "test-guest") { Get-ChildItem "E:\CoopAnything\$dd\BepInEx" -Filter "coop_*" -File | ForEach-Object { $_.Delete() } }
$common = "-screen-fullscreen 0 -screen-width 1280 -screen-height 720 -coopwin 1280x720 -coopwindowed -coopdrive"
"host pid " + (Start-Oculto "E:\CoopAnything\test-host" "$common -coopbot -coopscenario none")
Start-Sleep 8
"guest pid " + (Start-Oculto "E:\CoopAnything\test-guest" "$common -coopdriveport 7801")
Start-Sleep 45
function Anf($c) { & "$q\drive.ps1" host $c }
function Inv($c) { & "$q\drive.ps1" guest $c }
Anf "coop create" | Out-Null; Start-Sleep 6
"host: " + (Anf "coop status")
Anf "f2 on" | Out-Null; Start-Sleep 2; Anf "f2page _showNews false" | Out-Null; Start-Sleep 1
Anf "shot $Out\01_menu_anfitrion.png" | Out-Null
Anf "f2 off" | Out-Null
Inv "coop code IPAAKU" | Out-Null
Start-Sleep 40
"guest: " + (Inv "coop status")
"host: " + (Anf "coop status")
Start-Sleep 2
Get-Process Moonlighter -ErrorAction SilentlyContinue | Stop-Process -Force
Get-ChildItem $Out -Filter *.png | Select-Object Name, Length
