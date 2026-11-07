param([string[]]$Who = @("host","guest"), [int]$Rounds = 25)
# Each listed player walks (around holes) to the nearest enemy and attacks, until the room is clear.
$q = $PSScriptRoot
function Nearest($info) {
    if ($info -notmatch " pos=(-?\d+),(-?\d+)") { return $null }
    $px = [int]$Matches[1]; $py = [int]$Matches[2]
    $en = (($info -split " enemies=\[")[1] -split "\]")[0]
    $best = $null; $bd = 1e9
    foreach ($e in ($en -split ";")) {
        if ($e -match "@(-?\d+),(-?\d+)") { $d = [math]::Pow([int]$Matches[1] - $px, 2) + [math]::Pow([int]$Matches[2] - $py, 2); if ($d -lt $bd) { $bd = $d; $best = @([int]$Matches[1], [int]$Matches[2], [math]::Sqrt($d)) } }
    }
    return $best
}
for ($r = 0; $r -lt $Rounds; $r++) {
    $left = 0
    foreach ($w in $Who) {
        $i = & "$q\drive.ps1" $w info
        $t = Nearest $i
        if (-not $t) { continue }
        $left++
        if ($t[2] -gt 30) { & "$q\drive.ps1" $w "goto $($t[0]) $($t[1] - 14)" | Out-Null }
    }
    if ($left -eq 0) { "room clear after $r rounds"; break }
    Start-Sleep -Milliseconds 1500
    foreach ($k in 1..4) { foreach ($w in $Who) { & "$q\drive.ps1" $w "press Action1 0.12" | Out-Null }; Start-Sleep -Milliseconds 350 }
}
"HOST : " + ((& "$q\drive.ps1" host info) -replace "stick=.*?injected=\d+ ", "")
"GUEST: " + ((& "$q\drive.ps1" guest info) -replace "stick=.*?injected=\d+ ", "")
