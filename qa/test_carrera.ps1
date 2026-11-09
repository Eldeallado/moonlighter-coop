# Race rule, two copies on the hidden desktop: separate rooms, waiting at the way down, going down together.
param([string]$Out = "$env:TEMP\coop_carrera")
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
if (($bits -band 64) -eq 0) { "carrera on: " + (Anf "rule 6") }
foreach ($w in "host", "guest") { & "$q\drive.ps1" $w "inv on" | Out-Null }
Anf "dungeon Golem" | Out-Null
Start-Sleep 35
"host  " + (Anf "racestate")
"guest " + (Inv "racestate")
# The guest goes through a door on its own; the host stays.
$i = Inv "info"; $doors = [regex]::Match($i, 'doors=\[([^\]]*)\]').Groups[1].Value
$m = [regex]::Match($doors, 'LevelDoor(\w+)@[^;]*?->(\d+,\d+)(?!\(closed\))')
"puerta del invitado: " + $m.Groups[1].Value
if ($m.Success) { Inv ("door " + $m.Groups[1].Value) | Out-Null; Start-Sleep 8 }
"host  " + (Anf "racestate")
"guest " + (Inv "racestate")
Inv "shot $Out\01_invitado_otra_sala.png" | Out-Null
# The host reaches the way down first: it must wait, not travel.
Anf "nextfloor" | Out-Null; Start-Sleep 4
"host tras llegar: " + (Anf "racestate")
"guest: " + (Inv "racestate")
Anf "shot $Out\02_anfitrion_esperando.png" | Out-Null; Start-Sleep 2
# The guest arrives: both go down.
Inv "nextfloor" | Out-Null; Start-Sleep 2
Inv "shot $Out\03_resultado.png" | Out-Null
Start-Sleep 35
"host piso 2: " + (Anf "racestate")
"guest piso 2: " + (Inv "racestate")
# The guest falls: as a ghost it follows the host from room to room.
Inv "inv off" | Out-Null; Inv "hurt 9999" | Out-Null; Start-Sleep 8
"guest caído: " + (Inv "racestate")
$i = Anf "info"; $doors = [regex]::Match($i, 'doors=\[([^\]]*)\]').Groups[1].Value
$m = [regex]::Match($doors, 'LevelDoor(\w+)@[^;]*?->(\d+),(\d+)')
if ($m.Success) { Anf ("goroom " + $m.Groups[2].Value + " " + $m.Groups[3].Value) | Out-Null; Start-Sleep 6 }
"host se mueve: " + (Anf "racestate")
"guest fantasma: " + (Inv "racestate")
Inv "shot $Out\04_fantasma_sigue.png" | Out-Null; Start-Sleep 2
Get-Process Moonlighter -ErrorAction SilentlyContinue | Stop-Process -Force
Get-ChildItem $Out -Filter *.png | Select-Object Name, Length
