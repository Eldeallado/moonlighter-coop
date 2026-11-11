# Adaptador TCP<->UDP (Puente): el invitado entra con un código «PU» que apunta al anfitrión en este mismo PC,
# así todo va por UDP. Luego se pierde el 20 % de los paquetes a propósito y tienen que seguir jugando juntos.
param([string]$Out = "$env:TEMP\coop_puente")
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
Anf "coop create" | Out-Null; Start-Sleep 8
"host puente: " + (Anf "puente")
$code = (Anf "puenteprueba").Trim()
"código de prueba: $code"
Inv "coop code $code" | Out-Null
Start-Sleep 30
"guest: " + (Inv "coop status")
"guest puente: " + (Inv "puente")
"host: " + (Anf "coop status")
Anf "f2 on" | Out-Null; Start-Sleep 1; Anf "f2page _showNews false" | Out-Null; Start-Sleep 1
Anf "shot $Out\01_menu_anfitrion.png" | Out-Null
Anf "f2 off" | Out-Null
# Con pérdidas: los dos al mismo tiempo, y a la mazmorra juntos
Anf "perdida 0.2" | Out-Null; Inv "perdida 0.2" | Out-Null
foreach ($w in "host", "guest") { & "$q\drive.ps1" $w "inv on" | Out-Null }
Anf "dungeon Golem" | Out-Null
Start-Sleep 45
"host info:  " + ((Anf "info") -replace 'doors=.*','')
"guest info: " + ((Inv "info") -replace 'doors=.*','')
"guest: " + (Inv "coop status")
"guest puente: " + (Inv "puente")
"host puente: " + (Anf "puente")
Inv "shot $Out\02_invitado_mazmorra.png" | Out-Null
Start-Sleep 2
Get-Process Moonlighter -ErrorAction SilentlyContinue | Stop-Process -Force
Get-ChildItem $Out -Filter *.png | Select-Object Name, Length
Select-String -Path "E:\CoopAnything\test-guest\BepInEx\LogOutput.log","E:\CoopAnything\test-host\BepInEx\LogOutput.log" -Pattern "puente" | Select-Object -Last 10 | ForEach-Object { $_.Line }
