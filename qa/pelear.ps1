param([string]$Quien = "guest", [int]$Segundos = 60)
# One player walks through the floor fighting whatever it finds (the other follows through the doors).
$q = $PSScriptRoot
$fin = (Get-Date).AddSeconds($Segundos); $vistas = @{}
& "$q\drive.ps1" $Quien "inv on" | Out-Null
while ((Get-Date) -lt $fin) {
    $i = & "$q\drive.ps1" $Quien info
    $zona = [regex]::Match("$i", 'zone=([^ ]*)').Groups[1].Value
    if (-not $vistas.ContainsKey($zona)) { $vistas[$zona] = Get-Date }
    $em = [regex]::Match([regex]::Match("$i", 'enemies=\[([^\]]*)\]').Groups[1].Value, '@(-?\d+),(-?\d+)')
    if ($em.Success) {
        & "$q\drive.ps1" $Quien "inv on;;allow;;tp $([int]$em.Groups[1].Value + 26) $($em.Groups[2].Value);;stick -1 0" | Out-Null; Start-Sleep -Milliseconds 120
        & "$q\drive.ps1" $Quien "stop;;press Action1 0.1" | Out-Null; continue
    }
    $it = [regex]::Match("$i", 'items=\[[^@\]]*@(-?\d+),(-?\d+)')
    if ($it.Success) { & "$q\drive.ps1" $Quien "tp $($it.Groups[1].Value) $($it.Groups[2].Value)" | Out-Null; Start-Sleep -Milliseconds 300; continue }
    $doors = [regex]::Matches([regex]::Match("$i", 'doors=\[([^\]]*)\]').Groups[1].Value, 'LevelDoor(\w+)@[^;]*?->(\d+,\d+)(\(closed\))?') | Where-Object { -not $_.Groups[3].Success }
    $vk = @($vistas.Keys)
    $pick = $doors | Where-Object { $d = $_.Groups[2].Value; -not ($vk | Where-Object { $_ -like "*:$d" }) } | Select-Object -First 1
    if (-not $pick) { $pick = $doors | Get-Random }
    if ($pick) { & "$q\drive.ps1" $Quien "door $($pick.Groups[1].Value)" | Out-Null; Start-Sleep 2 }
}
& "$q\drive.ps1" $Quien "stop" | Out-Null
