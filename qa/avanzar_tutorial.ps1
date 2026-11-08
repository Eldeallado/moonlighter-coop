param([int]$Minutos = 15)
# Plays the host through the tutorial: skips dialogues, fights whatever is in the room, then takes an unvisited open door.
$q = $PSScriptRoot
$fin = (Get-Date).AddMinutes($Minutos)
$vistas = @{}
$ultimo = ""
& "$q\drive.ps1" host "inv on" | Out-Null
while ((Get-Date) -lt $fin) {
    & "$q\drive.ps1" host "gpress Submit 0.15;;gpress Action1 0.15" | Out-Null
    $i = & "$q\drive.ps1" host info
    $t = & "$q\drive.ps1" host tutorial
    $estado = [regex]::Match($t, 'slot=\d+').Value + " " + [regex]::Match($i, 'zone=[^ ]*').Value
    if ($estado -ne $ultimo) { "$(Get-Date -Format HH:mm:ss) $estado"; $ultimo = $estado }
    if ($t -match 'slot=4') { "TUTORIAL TERMINADO"; break }
    $zona = [regex]::Match($i, 'zone=([^ ]*)').Groups[1].Value
    if ($zona -like 'dungeon*') {
        $vistas[$zona] = $true
        $en = [regex]::Match($i, 'enemies=\[([^\]]*)\]').Groups[1].Value
        $pm = [regex]::Match($i, ' pos=(-?\d+),(-?\d+)')
        $em = [regex]::Match($en, '@(-?\d+),(-?\d+)')
        if ($em.Success) {
            & "$q\drive.ps1" host "goto $($em.Groups[1].Value) $($em.Groups[2].Value)" | Out-Null
            Start-Sleep 2
            for ($k = 0; $k -lt 4; $k++) { & "$q\drive.ps1" host "press Action1 0.2" | Out-Null; Start-Sleep -Milliseconds 400 }
            continue
        }
        # Items on the floor: walk over them.
        $it = [regex]::Match($i, 'items=\[[^@\]]*@(-?\d+),(-?\d+)')
        if ($it.Success) { & "$q\drive.ps1" host "goto $($it.Groups[1].Value) $($it.Groups[2].Value)" | Out-Null; Start-Sleep 3; continue }
        $doors = [regex]::Matches([regex]::Match($i, 'doors=\[([^\]]*)\]').Groups[1].Value, 'LevelDoor(\w+)@[^;]*?->(\d+,\d+)(\(closed\))?') | Where-Object { -not $_.Groups[3].Success }
        $pick = $doors | Where-Object { -not ($vistas.Keys | Where-Object { $_ -like "*:$($_.Groups[2].Value)*" }) } | Select-Object -First 1
        if (-not $pick) { $pick = $doors | Get-Random }
        if ($pick) { & "$q\drive.ps1" host "door $($pick.Groups[1].Value)" | Out-Null; Start-Sleep 6 }
        else { & "$q\drive.ps1" host "stick 1 0" | Out-Null; Start-Sleep 2; & "$q\drive.ps1" host "stop" | Out-Null }
    }
    else { Start-Sleep 2 }
}
"guest: " + (& "$q\drive.ps1" guest tutorial)
