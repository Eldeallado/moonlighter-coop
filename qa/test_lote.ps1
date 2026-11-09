# Two copies on the hidden desktop: party HUD (portraits, name tags, lag mark), asking a partner for a piece,
# and the run history after a trip to the dungeon and back.
param([string]$Out = "$env:TEMP\coop_lote")
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
Anf "coop listen" | Out-Null; Start-Sleep 1
Inv "coop ip 127.0.0.1" | Out-Null
Start-Sleep 35
"guest: " + (Inv "coop status")
foreach ($w in "host", "guest") { & "$q\drive.ps1" $w "inv on" | Out-Null }
# The guest walks a bit away so the name tag shows; fake a slow link for the host on the guest's screen.
Start-Sleep 2
Inv "lagfake 0 420" | Out-Null
Inv "shot $Out\01_hud_invitado.png" | Out-Null; Anf "shot $Out\02_hud_anfitrion.png" | Out-Null; Start-Sleep 2
# Asking: the guest asks the host for weapon 1; the host says yes with the pad.
Inv "ask 0 0" | Out-Null; Start-Sleep 2
Anf "shot $Out\03_pregunta.png" | Out-Null; Start-Sleep 1
Anf "coop pad up" | Out-Null; Start-Sleep 2
Inv "shot $Out\04_respuesta_invitado.png" | Out-Null; Start-Sleep 1
# A run: into the dungeon and back home.
Anf "dungeon Golem" | Out-Null; Start-Sleep 30
Anf "town" | Out-Null; Start-Sleep 30
"historial anfitrion: " + (Anf "history")
"historial invitado: " + (Inv "history")
Anf "uishow off" | Out-Null; Anf "f2 on" | Out-Null; Anf "f2page _showHist" | Out-Null; Start-Sleep 1
Anf "shot $Out\05_historial.png" | Out-Null; Start-Sleep 2
Get-Process Moonlighter -ErrorAction SilentlyContinue | Stop-Process -Force
Get-ChildItem $Out -Filter *.png | Select-Object Name, Length
