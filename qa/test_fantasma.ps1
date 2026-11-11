# El invitado muere en la mazmorra: tiene que quedarse de fantasma (y el anfitrión verlo así).
# Mide también cuánto ocupa su pose de fantasma (por Steam, más de 1200 bytes no salían).
param([string]$Out = "$env:TEMP\coop_fantasma")
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
Anf "coop create" | Out-Null; Start-Sleep 4
Inv "coop ip 127.0.0.1" | Out-Null
Start-Sleep 35
"guest: " + (Inv "coop status")
Anf "inv on" | Out-Null
Anf "dungeon Golem" | Out-Null
Start-Sleep 35
"invitado vivo:  " + (Inv "posesize")
"anfitrión vivo: " + (Anf "posesize")
Inv "hurt 9999" | Out-Null
Start-Sleep 10
"invitado muerto:  " + (Inv "posesize")
"anfitrión ve:     " + (Anf "posesize")
Inv "shot $Out\01_invitado_fantasma.png" | Out-Null
Anf "shot $Out\02_anfitrion_ve.png" | Out-Null
Start-Sleep 2
Get-Process Moonlighter -ErrorAction SilentlyContinue | Stop-Process -Force
Select-String -Path "E:\CoopAnything\test-guest\BepInEx\LogOutput.log" -Pattern "\[downed\]" | Select-Object -Last 4 | ForEach-Object { $_.Line }
