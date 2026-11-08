param([string]$Speed = "4", [switch]$Solo)
# Hidden test: a new game, walk the tutorial dungeon to the first room with enemies, then die there and watch
# whether the tutorial resets the room (it should) or freezes.
$q = $PSScriptRoot
. "$q\oculto.ps1"
Get-Process Moonlighter -ErrorAction SilentlyContinue | Where-Object { $_.Path -like 'E:\CoopAnything\*' } | ForEach-Object { taskkill /PID $_.Id /F | Out-Null }
Start-Sleep 2
$common = "-screen-fullscreen 0 -screen-width 640 -screen-height 360 -coopwin 640x360 -coopwindowed -coopdrive -coopspeed $Speed"
$rol = if ($Solo) { "" } else { " -coop host" }
Start-Oculto "E:\CoopAnything\test-host" "$common$rol" | Out-Null
Start-Sleep 25
& "$q\drive.ps1" host "newgame" | Out-Null
for ($i = 0; $i -lt 60; $i++) {
    & "$q\drive.ps1" host "gpress Submit 0.1;;gpress Action1 0.1" | Out-Null
    $inf = & "$q\drive.ps1" host info
    if ($inf -match 'zone=dungeon' -and $inf -match 'canMove=True') { break }
    Start-Sleep 2
}
"en mazmorra: " + ([regex]::Match($inf, 'zone=\S+').Value)
& "$q\drive.ps1" host "hurt 999" | Out-Null
for ($i = 0; $i -lt 8; $i++) { Start-Sleep 3; "t=$(($i+1)*3)s " + (& "$q\drive.ps1" host "deathinfo;;ondie") }
