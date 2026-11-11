# El Errante, dos copias en el escritorio oculto: al invitado solo le sale cuando le sale al anfitrión.
param([string]$Out = "$env:TEMP\coop_errante")
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
$bits = [int]([regex]::Match((Anf "roleinfo"), 'rules=(\d+)').Groups[1].Value)
if (($bits -band 64) -ne 0) { "carrera off: " + (Anf "rule 6") }
foreach ($w in "host", "guest") { & "$q\drive.ps1" $w "inv on" | Out-Null; & "$q\drive.ps1" $w "setculture Golem vivo" | Out-Null }
Anf "gold 5000" | Out-Null
Anf "dungeon Golem" | Out-Null
Start-Sleep 35
# Sala de inicio: el juego no lo saca aquí; ir a otra sala los dos (puertas estilo Isaac)
$i = Anf "info"; $doors = [regex]::Match($i, 'doors=\[([^\]]*)\]').Groups[1].Value
$m = [regex]::Match($doors, 'LevelDoor(\w+)@[^;]*?->(\d+,\d+)(?!\(closed\))')
if ($m.Success) { Anf ("door " + $m.Groups[1].Value) | Out-Null; Start-Sleep 10 }
"antes host  " + (Anf "errinfo")
"antes guest " + (Inv "errinfo")
# El reloj propio del invitado ya no cuenta: pedirle al invitado que lo saque no debe hacer nada
Inv "errante" | Out-Null; Anf "errante" | Out-Null; Start-Sleep 5   # (el atajo "dungeon" no carga los enemigos comunes: así se cargan)
"invitado intenta: " + (Inv "errante")
Start-Sleep 2
"guest tras intentar " + (Inv "errinfo")
"anfitrión lo saca: " + (Anf "errante")
Start-Sleep 3
"después host  " + (Anf "errinfo")
"después guest " + (Inv "errinfo")
Anf "shot $Out\01_anfitrion.png" | Out-Null
Inv "shot $Out\02_invitado.png" | Out-Null
Start-Sleep 2
Get-Process Moonlighter -ErrorAction SilentlyContinue | Stop-Process -Force
Get-ChildItem $Out -Filter *.png | Select-Object Name, Length
Select-String -Path "E:\CoopAnything\test-guest\BepInEx\LogOutput.log" -Pattern "errante" | Select-Object -Last 5 | ForEach-Object { $_.Line }
