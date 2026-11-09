param([string]$Out = "$env:TEMP\coop_menus", [switch]$KeepOpen)
# Screenshots of the mod's windows on the hidden desktop (one host copy): F2 and its pages, F3, and the
# panels that can be shown on demand (roles, call, summary, chat). For checking the look after a change.
$q = $PSScriptRoot
. "$q\oculto.ps1"
New-Item -ItemType Directory -Force $Out | Out-Null
Get-Process Moonlighter -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep 2
function D($c) { & "$q\drive.ps1" host $c }
function Shot($name) { Start-Sleep -Milliseconds 700; D "shot $Out\$name.png" | Out-Null; Start-Sleep -Milliseconds 900 }
"pid " + (Start-Oculto "E:\CoopAnything\test-host" "-screen-fullscreen 0 -screen-width 1280 -screen-height 720 -coopwin 1280x720 -coopwindowed -coopdrive -coopbot -coopscenario none")
Start-Sleep 40
D "f2 on" | Out-Null; Shot "01_f2_menu"
D "coop create" | Out-Null; Start-Sleep 3; Shot "02_f2_partida"
D "f2page _showNews" | Out-Null; Shot "03_novedades"
D "coop pad b" | Out-Null; Start-Sleep -Milliseconds 400
D "f2page _showAch" | Out-Null; D "coop pad down" | Out-Null; Shot "04_logros"
D "coop pad b" | Out-Null; Start-Sleep -Milliseconds 400
D "f2page _showRules" | Out-Null; Shot "05_reglas"
D "coop pad b" | Out-Null; Start-Sleep -Milliseconds 400
D "f2page _showGive" | Out-Null; Shot "06_dar"
D "coop pad b" | Out-Null; D "f2 off" | Out-Null
D "menu on" | Out-Null; Shot "07_f3"
D "menu off" | Out-Null
D "uishow roles" | Out-Null; Shot "08_roles"
D "uishow call" | Out-Null; Shot "09_aviso"
D "uishow summary" | Out-Null; Shot "10_resumen"
D "uishow chat" | Out-Null; Shot "11_chat"
D "uishow off" | Out-Null
if (-not $KeepOpen) { Get-Process Moonlighter -ErrorAction SilentlyContinue | Stop-Process -Force }
Get-ChildItem $Out -Filter *.png | Select-Object Name, Length
