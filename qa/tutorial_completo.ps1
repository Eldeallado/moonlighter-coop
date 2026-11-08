param([int]$Minutos = 50, [string[]]$Quien = @("host", "guest"), [string]$Fotos = "")
# Plays both players through the WHOLE tutorial (dungeon, shop, Crazy Pete) at the same time, each in its own
# tutorial. Logs every change and flags anyone stuck for 3 minutes. Run on the hidden desktop (tutorial_oculto.ps1).
$q = $PSScriptRoot
function D($w, $c) { & "$q\drive.ps1" $w $c }
function Pos($s) { $m = [regex]::Match("$s", 'pos=\((-?\d+)[\.\d]*, (-?\d+)'); if ($m.Success) { "$($m.Groups[1].Value) $($m.Groups[2].Value)" } }
$fin = (Get-Date).AddMinutes($Minutos)
$st = @{}
foreach ($w in $Quien) { $st[$w] = @{ vistas = @{}; ultimo = ""; desde = Get-Date; hecho = $false; pos = ""; quieto = 0 } }
while ((Get-Date) -lt $fin -and ($st.Values | Where-Object { -not $_.hecho })) {
    foreach ($w in $Quien) {
        $s = $st[$w]; if ($s.hecho) { continue }
        $t = D $w "tutorial"; $i = D $w "info"
        $scene = [regex]::Match("$t", 'scene=(\S+)').Groups[1].Value
        $panel = [regex]::Match("$t", 'panel=(\S+)').Groups[1].Value
        $zona = [regex]::Match("$i", 'zone=([^ ]*)').Groups[1].Value
        $mueve = "$i" -match 'canMove=True'
        $estado = "$([regex]::Match("$t", 'slot=\d+').Value) $scene $panel $zona"
        if ($estado -ne $s.ultimo) { "$(Get-Date -Format HH:mm:ss) $w $estado"; $s.ultimo = $estado; $s.desde = Get-Date; if ($Fotos) { D $w "shot $Fotos/$w`_$(Get-Date -Format HHmmss).png" | Out-Null } }
        elseif (((Get-Date) - $s.desde).TotalMinutes -gt 3) { "$(Get-Date -Format HH:mm:ss) $w ATASCADO en: $estado"; if ($Fotos) { D $w "shot $Fotos/$w`_atasco.png" | Out-Null }; $s.desde = Get-Date }
        if ("$t" -match 'slot=4' -and $scene -eq '-') { "$(Get-Date -Format HH:mm:ss) $w TUTORIAL TERMINADO"; $s.hecho = $true; continue }
        D $w "inv on" | Out-Null   # the tutorial drops invincibility after 5 s: renew it
        # Panels the tutorial opens or asks for.
        if ($panel -eq 'Inventory_Chest') { D $w "gpress Action4 0.1;;gpress Action2 0.1" | Out-Null; continue }
        if ($panel -eq 'HeroInventoryPanel') { D $w "gpress RightTrigger 0.15" | Out-Null; Start-Sleep 1; continue }
        if ($panel -eq 'Notebook_Panel') { D $w "gpress Action4 0.1" | Out-Null; Start-Sleep 1; D $w "gpress Action2 0.1" | Out-Null; continue }
        D $w "gpress Action1 0.05;;gpress Submit 0.05" | Out-Null   # dialogues, picture pop-ups (also while Will "can move" in cutscenes)
        if (-not $mueve) { continue }
        if ($scene -eq 'TutorialScene2' -and $zona -eq 'town:shop') {
            $c = [int]("$(D $w 'tfield _count')" -replace '\D', '0')
            $shop = D $w "shopinfo"
            if ($c -lt 3) { D $w "goto -1920 -370" | Out-Null; continue }   # downstairs, where Zenon waits
            if ($c -eq 3 -and "$shop" -match 'open=False') { D $w "openshop" | Out-Null; continue }
            if ("$shop" -match 'atCash=True') { D $w "charge" | Out-Null; continue }
            if ($c -eq 8) { D $w "gpress Action1 0.05" | Out-Null; Start-Sleep -Milliseconds 300; D $w "press Action4 0.1" | Out-Null; continue }   # open the bag
            continue
        }
        if ($scene -eq 'TutorialScene3' -and $zona -like 'town*') { D $w "portal" | Out-Null; Start-Sleep 3; continue }
        if ($zona -notlike 'dungeon*') { continue }
        if ($scene -eq 'TutorialScene3') {
            # Camp room: talk to Crazy Pete, then to his minion, then go home with the pendant.
            $pete = Pos (D $w "tfield _peteTransform"); $min = Pos (D $w "tfield _minionTransform")
            $c = "$(D $w 'tfield _count')"
            if ("$(D $w 'tfield campRoom')" -match 'active=True' -and $zona -eq "$($s.campZone)") {
                if ($c -match '^\d+$' -and [int]$c -ge 5) { D $w "pendant" | Out-Null; Start-Sleep 2; continue }
                $obj = if ($c -match '^1$') { $pete } else { $min }
                if ($obj) { D $w "goto $obj" | Out-Null; Start-Sleep 2; D $w "press Action1 0.1" | Out-Null; continue }
            }
        }
        # Dungeon: fight (not forever), pick up loot, take a new open door; break pots when stuck.
        if (-not $s.vistas.ContainsKey($zona)) { $s.vistas[$zona] = Get-Date }
        $enSala = ((Get-Date) - $s.vistas[$zona]).TotalSeconds
        $pos = [regex]::Match("$i", ' pos=(-?\d+),(-?\d+)')
        if ($pos.Value -eq $s.pos) { $s.quieto++ } else { $s.quieto = 0; $s.pos = $pos.Value }
        if ($s.quieto -ge 5) {
            for ($k = 0; $k -lt 6; $k++) { D $w "press Action1 0.1" | Out-Null }
            if ($s.quieto -ge 10) { $rm = [regex]::Match("$i", 'room=(-?\d+),(-?\d+)'); D $w "tp $($pos.Groups[1].Value) $($rm.Groups[2].Value)" | Out-Null; $s.quieto = 0 }
            continue
        }
        $em = [regex]::Match([regex]::Match("$i", 'enemies=\[([^\]]*)\]').Groups[1].Value, '@(-?\d+),(-?\d+)')
        $abiertas = [regex]::Matches([regex]::Match("$i", 'doors=\[([^\]]*)\]').Groups[1].Value, 'LevelDoor\w+@[^;]*?->\d+,\d+(?!\(closed\))(;|$)').Count
        if ($em.Success -and $enSala -ge 20 -and $abiertas -eq 0) {   # doors stay shut until it dies: stand next to it and hit it
            D $w "allow;;tp $([int]$em.Groups[1].Value + 28) $($em.Groups[2].Value);;stick -1 0" | Out-Null; Start-Sleep -Milliseconds 150
            D $w "stop;;press Action1 0.1" | Out-Null; continue
        }
        if ($em.Success -and $enSala -lt 20) { D $w "goto $($em.Groups[1].Value) $($em.Groups[2].Value)" | Out-Null; for ($k = 0; $k -lt 3; $k++) { D $w "press Action1 0.1" | Out-Null }; continue }
        $it = [regex]::Match("$i", 'items=\[[^@\]]*@(-?\d+),(-?\d+)')
        if ($it.Success -and $enSala -lt 30) { D $w "goto $($it.Groups[1].Value) $($it.Groups[2].Value)" | Out-Null; continue }
        $doors = [regex]::Matches([regex]::Match("$i", 'doors=\[([^\]]*)\]').Groups[1].Value, 'LevelDoor(\w+)@[^;]*?->(\d+,\d+)(\(closed\))?') | Where-Object { -not $_.Groups[3].Success }
        $vk = @($s.vistas.Keys)
        $pick = $doors | Where-Object { $d = $_.Groups[2].Value; -not ($vk | Where-Object { $_ -like "*:$d" }) } | Select-Object -First 1
        if (-not $pick) { $pick = $doors | Get-Random }
        if ($pick) { D $w "door $($pick.Groups[1].Value)" | Out-Null }
        if ($scene -eq 'TutorialScene3' -and "$(D $w 'tfield campRoom')" -match 'active=True') { if (-not $s.campZone) { } }
    }
    # Remember which zone is the camp room once someone is in it (Scene11 locks it).
    foreach ($w in $Quien) { if ($st[$w].hecho) { continue }; if ("$(D $w 'tfield _peteTriggrer')" -match 'active=True' -and "$(D $w 'tfield _sparkleEmit')" -notmatch '^null') { $st[$w].campZone = [regex]::Match("$(D $w info)", 'zone=([^ ]*)').Groups[1].Value } }
    Start-Sleep -Milliseconds 500
}
foreach ($w in $Quien) { "$w final: " + (D $w tutorial) }
