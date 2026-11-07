param(
    [int]$GuestDelay = 20,
    [int]$Wait = 110,
    [switch]$KeepOpen
)
. "$PSScriptRoot\windows.ps1"
# Starts a host and a guest copy with -coopbot and checks they see each other.
$ws = "E:\CoopAnything"
$hostDir = "$ws\test-host"; $guestDir = "$ws\test-guest"
$common = "-screen-fullscreen 0 -screen-width 800 -screen-height 450 -coopbot"

Get-Process Moonlighter -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep 2
Remove-Item "$hostDir\BepInEx\coop_*", "$guestDir\BepInEx\coop_*" -ErrorAction SilentlyContinue

$h = Start-Copy $hostDir "$common -coop host" -1920
Start-Sleep $GuestDelay
$g = Start-Copy $guestDir "$common -coop join 127.0.0.1" -1100
Start-Sleep $Wait

function Read-Status($dir, $tag) {
    $f = "$dir\BepInEx\coop_${tag}_status.txt"
    $o = @{}
    if (Test-Path $f) { Get-Content $f | ForEach-Object { $k, $v = $_ -split "=", 2; $o[$k] = $v } }
    return $o
}
function XY($s) { if ($s -match "^(-?[\d\.]+),(-?[\d\.]+)$") { return @([double]$Matches[1], [double]$Matches[2]) } return $null }

$results = @()
function Check($name, $ok, $detail) {
    $script:results += [pscustomobject]@{ Check = $name; Result = $(if ($ok) { "PASS" } else { "FAIL" }); Detail = $detail }
}

$hs = Read-Status $hostDir "host"; $gs = Read-Status $guestDir "join"
Check "guest connected" ($gs.connected -eq "True") "connected=$($gs.connected)"
Check "host sent save" ($hs.save_sent -eq "True") "save_sent=$($hs.save_sent)"
Check "guest loaded host save" ($gs.guest_loaded -eq "True") "guest_loaded=$($gs.guest_loaded)"
Check "same game slot" (($gs.slot -eq $hs.slot) -and ($hs.slot -ne "0")) "host=$($hs.slot) guest=$($gs.slot)"
Check "guest in town" ($gs.zone -eq "town") "zone=$($gs.zone)"
Check "guest sees host" ($gs.puppet_visible -eq "True") "visible=$($gs.puppet_visible) parts=$($gs.puppet_parts)"
Check "host sees guest" ($hs.puppet_visible -eq "True") "visible=$($hs.puppet_visible) parts=$($hs.puppet_parts)"
Check "puppet has sprites" ([int]$gs.puppet_parts -ge 1) "parts=$($gs.puppet_parts) misses=$($gs.sprite_misses)"

# Position: sample both files close together several times; the host bot moves every 3 s.
$best = 1e9
for ($i = 0; $i -lt 6; $i++) {
    $hs = Read-Status $hostDir "host"; $gs = Read-Status $guestDir "join"
    $a = XY $hs.hero; $b = XY $gs.puppet
    if ($a -and $b) { $d = [math]::Sqrt(($a[0]-$b[0])*($a[0]-$b[0]) + ($a[1]-$b[1])*($a[1]-$b[1])); if ($d -lt $best) { $best = $d } }
    Start-Sleep -Milliseconds 700
}
Check "puppet follows host position" ($best -lt 15) ("closest distance {0:F1}" -f $best)
Check "ping measured" ([double]($gs.ping_ms) -gt -1) "ping=$($gs.ping_ms)"

$results | Format-Table -AutoSize | Out-String -Width 160
$pass = ($results | Where-Object Result -eq "PASS").Count
"SUITE: $pass/$($results.Count) passed"
if (-not $KeepOpen) { Get-Process Moonlighter -ErrorAction SilentlyContinue | Stop-Process -Force }



