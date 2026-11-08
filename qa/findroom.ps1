param([string]$Pattern = "Turret", [string]$Who = "host", [int]$MaxRooms = 12)
# Walks $Who through doors (unvisited first) until the room has an enemy whose name matches $Pattern.
$q = $PSScriptRoot
$seen = @{}
for ($n = 0; $n -lt $MaxRooms; $n++) {
    $i = & "$q\drive.ps1" $Who info
    $zone = [regex]::Match($i, 'zone=(\S+)').Groups[1].Value
    $room = ($zone -split ':')[-1]
    $seen[$room] = $true
    $enemies = [regex]::Match($i, 'enemies=\[([^\]]*)\]').Groups[1].Value
    if ($enemies -match $Pattern) { "FOUND in $zone : $enemies"; return }
    $doorText = [regex]::Match($i, 'doors=\[([^\]]*)\]').Groups[1].Value
    $doors = @()
    foreach ($m in [regex]::Matches($doorText, 'LevelDoor(\w+)@[^;]*?->(\d+,\d+)(\(closed\))?')) {
        if (-not $m.Groups[3].Success) { $doors += [pscustomobject]@{ Dir = $m.Groups[1].Value; Dest = $m.Groups[2].Value } }
    }
    if ($doors.Count -eq 0) { "no open door in $zone (enemies: $enemies)"; return }
    $pick = $doors | Where-Object { -not $seen.ContainsKey($_.Dest) } | Select-Object -First 1
    if (-not $pick) { $pick = $doors | Get-Random }
    & "$q\drive.ps1" $Who "door $($pick.Dir)" | Out-Null
    Start-Sleep 9
}
"not found after $MaxRooms rooms"
