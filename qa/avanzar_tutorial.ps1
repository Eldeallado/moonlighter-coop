param([int]$Minutos = 20, [string[]]$Quien = @("host", "guest"), [string]$Fotos = "")
# Plays every given player through the tutorial at the same time: skips dialogues, fights whatever is in the room,
# picks up loot and takes an unvisited open door. Logs every change of state and flags anyone stuck for 3 minutes.
$q = $PSScriptRoot
$fin = (Get-Date).AddMinutes($Minutos)
$st = @{}
foreach ($w in $Quien) { $st[$w] = @{ vistas = @{}; ultimo = ""; desde = Get-Date; hecho = $false }; & "$q\drive.ps1" $w "inv on" | Out-Null }
function Foto($w, $tag) { if ($Fotos) { & "$q\drive.ps1" $w "shot $Fotos/$w`_$tag.png" | Out-Null } }
while ((Get-Date) -lt $fin -and ($st.Values | Where-Object { -not $_.hecho })) {
    foreach ($w in $Quien) {
        $s = $st[$w]; if ($s.hecho) { continue }
        & "$q\drive.ps1" $w "gpress Submit 0.1;;gpress Action1 0.1" | Out-Null
        $i = & "$q\drive.ps1" $w info
        $t = & "$q\drive.ps1" $w tutorial
        $estado = [regex]::Match($t, 'slot=\d+').Value + " " + [regex]::Match($t, 'panel=\S+').Value + " " + [regex]::Match($i, 'zone=[^ ]*').Value
        if ($estado -ne $s.ultimo) { "$(Get-Date -Format HH:mm:ss) $w $estado"; $s.ultimo = $estado; $s.desde = Get-Date; Foto $w ((Get-Date -Format HHmmss)) }
        elseif (((Get-Date) - $s.desde).TotalMinutes -gt 3) { "$(Get-Date -Format HH:mm:ss) $w ATASCADO en: $estado"; Foto $w "atasco"; $s.desde = Get-Date }
        if ($t -match 'slot=4') { "$(Get-Date -Format HH:mm:ss) $w TUTORIAL TERMINADO"; $s.hecho = $true; continue }
        $zona = [regex]::Match($i, 'zone=([^ ]*)').Groups[1].Value
        if ($zona -notlike 'dungeon*') { continue }
        $s.vistas[$zona] = $true
        $em = [regex]::Match([regex]::Match($i, 'enemies=\[([^\]]*)\]').Groups[1].Value, '@(-?\d+),(-?\d+)')
        if ($em.Success) {
            & "$q\drive.ps1" $w "goto $($em.Groups[1].Value) $($em.Groups[2].Value)" | Out-Null
            for ($k = 0; $k -lt 3; $k++) { & "$q\drive.ps1" $w "press Action1 0.1" | Out-Null }
            continue
        }
        $it = [regex]::Match($i, 'items=\[[^@\]]*@(-?\d+),(-?\d+)')
        if ($it.Success) { & "$q\drive.ps1" $w "goto $($it.Groups[1].Value) $($it.Groups[2].Value)" | Out-Null; continue }
        $doors = [regex]::Matches([regex]::Match($i, 'doors=\[([^\]]*)\]').Groups[1].Value, 'LevelDoor(\w+)@[^;]*?->(\d+,\d+)(\(closed\))?') | Where-Object { -not $_.Groups[3].Success }
        $vk = $s.vistas.Keys
        $pick = $doors | Where-Object { $d = $_.Groups[2].Value; -not ($vk | Where-Object { $_ -like "*:$d" }) } | Select-Object -First 1
        if (-not $pick) { $pick = $doors | Get-Random }
        if ($pick) { & "$q\drive.ps1" $w "door $($pick.Groups[1].Value)" | Out-Null }
    }
    Start-Sleep -Milliseconds 700
}
foreach ($w in $Quien) { "$w final: " + (& "$q\drive.ps1" $w tutorial) }
