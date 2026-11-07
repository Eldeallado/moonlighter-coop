param([ValidateSet("host","guest","guest2")]$Who = "guest", [int]$MaxRooms = 6)
# Walks $Who through doors (preferring rooms not seen yet) until a room has enemies. Prints both players' view.
$q = $PSScriptRoot
function Parse($info) {
    $o = @{}
    foreach ($k in "zone","pos","room","enemies","doors","items","hp","partner","ghost","partnerGhost") {
        if ($info -match " $k=(\[[^\]]*\]|\S+)") { $o[$k] = $Matches[1] } elseif ($info -match "^$k=(\S+)") { $o[$k] = $Matches[1] }
    }
    if ($info -match "zone=(\S+)") { $o.zone = $Matches[1] }
    return $o
}
$seen = @{}
for ($n = 0; $n -lt $MaxRooms; $n++) {
    $i = Parse (& "$q\drive.ps1" $Who info)
    $seen[$i.zone] = $true
    if ($i.enemies -and $i.enemies -ne "[]") { "ENEMIES in $($i.zone): $($i.enemies)"; break }
    $doors = ($i.doors.Trim("[]") -split ";") | Where-Object { $_ -and $_ -notmatch "closed" }
    $rx, $ry = $i.room -split ","
    $pick = $null
    foreach ($d in $doors) {
        if ($d -match "^(\w+)@(-?\d+),(-?\d+)->(\d+),(\d+)") {
            $dest = "$($Matches[4]),$($Matches[5])"
            $cand = @{ name = $Matches[1]; dest = $dest }
            if (-not ($seen.Keys | Where-Object { $_ -like "*:$dest" })) { $pick = $cand; break }
            if (-not $pick) { $pick = $cand }
        }
    }
    if (-not $pick) { "no open door in $($i.zone)"; break }
    # Push past the door, away from the room centre.
    $dx = $pick.x - [int]$rx; $dy = $pick.y - [int]$ry
    $len = [math]::Max(1, [math]::Sqrt($dx * $dx + $dy * $dy))
    $tx = [int]($pick.x + 90 * $dx / $len); $ty = [int]($pick.y + 90 * $dy / $len)
    & "$q\drive.ps1" $Who "door $($pick.name)" | Out-Null
    Start-Sleep 9
    "moved toward $($pick.dest)"
}
"HOST : " + (& "$q\drive.ps1" host info)
"GUEST: " + (& "$q\drive.ps1" guest info)

