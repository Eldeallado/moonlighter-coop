param([int]$Timeout = 300, [switch]$KeepOpen)
. "$PSScriptRoot\windows.ps1"
# Host finds a room with enemies; only the guest attacks. Enemies must die on both screens.
$ws = "E:\CoopAnything"
$hostDir = "$ws\test-host"; $guestDir = "$ws\test-guest"
$common = "-screen-fullscreen 0 -screen-width 800 -screen-height 450 -coopbot -coopscenario combat"
Get-Process Moonlighter -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep 2
Get-ChildItem "$hostDir\BepInEx", "$guestDir\BepInEx" -Filter "coop_*" -ErrorAction SilentlyContinue | ForEach-Object { $_.Delete() }
$h = Start-Copy $hostDir "$common -coop host" -1920
Start-Sleep 20
$g = Start-Copy $guestDir "$common -coop join 127.0.0.1" -1100
function Read-Status($dir, $tag) {
    $f = "$dir\BepInEx\coop_${tag}_status.txt"; $o = @{}
    if (Test-Path $f) { try { Get-Content $f -ErrorAction Stop | ForEach-Object { $k, $v = $_ -split "=", 2; $o[$k] = $v } } catch {} }
    return $o
}
$t0 = Get-Date; $last = ""; $cMatch = 0; $cDiff = 0; $maxGuestAliveSeen = 0
while (((Get-Date) - $t0).TotalSeconds -lt $Timeout) {
    $h = Read-Status $hostDir "host"; $g = Read-Status $guestDir "join"
    $line = "host=$($h.zone) alive=$($h.room_enemies_alive) | guest=$($g.zone) alive=$($g.room_enemies_alive) | hits $($g.enemy_hits_sent)->$($h.enemy_hits_applied) deaths $($h.enemy_deaths_sent)->$($g.enemy_deaths_applied) states=$($g.enemy_states_applied)"
    if ($line -ne $last) { "t={0,3:N0} $line" -f ((Get-Date) - $t0).TotalSeconds; $last = $line }
    if ([int]$g.room_enemies_alive -gt $maxGuestAliveSeen -and $g.zone -eq $h.zone) { $maxGuestAliveSeen = [int]$g.room_enemies_alive }
    if ($h.content_sig -and $g.content_sig -and $h.zone -eq $g.zone) { if ($h.content_sig -eq $g.content_sig) { $cMatch++ } else { $cDiff++ } }
    if ($h.combat_cleared -eq "True" -and $g.room_enemies_alive -eq "0" -and [int]$h.breaks_applied -ge 1) { Start-Sleep 2; break }
    Start-Sleep -Milliseconds 700
}
$h = Read-Status $hostDir "host"; $g = Read-Status $guestDir "join"
$results = @()
function Check($name, $ok, $detail) { $script:results += [pscustomobject]@{ Check = $name; Result = $(if ($ok) { "PASS" } else { "FAIL" }); Detail = $detail } }
Check "found a room with enemies" ($h.combat_seen -eq "True") "seen=$($h.combat_seen)"
Check "guest saw the same enemies" ($maxGuestAliveSeen -gt 0) "max alive at guest=$maxGuestAliveSeen"
Check "guest enemies copy the host" ([int]$g.enemy_states_applied -gt 20) "states=$($g.enemy_states_applied)"
Check "guest hits reached the host" (([int]$g.enemy_hits_sent -gt 0) -and ([int]$h.enemy_hits_applied -gt 0)) "sent=$($g.enemy_hits_sent) applied=$($h.enemy_hits_applied)"
Check "room cleared on host" ($h.combat_cleared -eq "True") "alive=$($h.room_enemies_alive)"
Check "guest broke a box, host box broke too" (([int]$g.breaks_sent -ge 1) -and ([int]$h.breaks_applied -ge 1)) "sent=$($g.breaks_sent) applied=$($h.breaks_applied)"
Check "rooms have the same things" ($cMatch -gt 0) "same=$cMatch different=$cDiff (different can be enemies dying between reads)"
Check "enemies dead on guest too" (($g.room_enemies_alive -eq "0") -and ([int]$g.enemy_deaths_applied -gt 0)) "alive=$($g.room_enemies_alive) deaths applied=$($g.enemy_deaths_applied)/$($h.enemy_deaths_sent)"
$results | Format-Table -AutoSize | Out-String -Width 200
"SUITE: $(($results | Where-Object Result -eq 'PASS').Count)/$($results.Count) passed"
if (-not $KeepOpen) { Get-Process Moonlighter -ErrorAction SilentlyContinue | Stop-Process -Force }


