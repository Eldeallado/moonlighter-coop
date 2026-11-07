param([int]$Timeout = 360, [switch]$KeepOpen)
. "$PSScriptRoot\windows.ps1"
# Combat with a mortal guest, shared loot pick-up, guest downed and revived, both down -> both home.
$ws = "E:\CoopAnything"
$hostDir = "$ws\test-host"; $guestDir = "$ws\test-guest"
$common = "-screen-fullscreen 0 -screen-width 800 -screen-height 450 -coopbot -coopscenario coop"
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
$t0 = Get-Date; $last = ""; $sawDowned = $false; $sawHostSeesDowned = $false; $wasInDungeon = $false; $bothHome = $false
while (((Get-Date) - $t0).TotalSeconds -lt $Timeout) {
    $h = Read-Status $hostDir "host"; $g = Read-Status $guestDir "join"
    $line = "host=$($h.zone) | guest=$($g.zone) hp=$($g.hp) min=$($g.hp_min) | loot $($h.loot_sent)->$($g.loot_shown) taken=$($g.loot_taken) removed@host=$($h.loot_removed) | down me=$($g.downed_me) hostSees=$($h.downed_partner) revives=$($g.revives)"
    if ($line -ne $last) { "t={0,3:N0} $line" -f ((Get-Date) - $t0).TotalSeconds; $last = $line }
    if ($g.downed_me -eq "True") { $sawDowned = $true }
    if ($h.downed_partner -eq "True") { $sawHostSeesDowned = $true }
    if ($h.zone -like "dungeon*") { $wasInDungeon = $true }
    if ($wasInDungeon -and [int]$g.revives -ge 1 -and $h.zone -like "town*" -and $g.zone -like "town*") { $bothHome = $true; $hk = Read-Status $hostDir "host"; $gk = Read-Status $guestDir "join"; break }
    Start-Sleep -Milliseconds 700
}
$h = Read-Status $hostDir "host"; $g = Read-Status $guestDir "join"
$results = @()
function Check($name, $ok, $detail) { $script:results += [pscustomobject]@{ Check = $name; Result = $(if ($ok) { "PASS" } else { "FAIL" }); Detail = $detail } }
Check "enemies dead on both" (([int]$g.enemy_deaths_applied -gt 0)) "deaths applied=$($g.enemy_deaths_applied)/$($h.enemy_deaths_sent)"
Check "enemies hurt the guest" ([double]$g.hp_min -lt 9999 -and $sawDowned) "lowest guest hp before dying=$($g.hp_min)"
Check "host loot appears at the guest" (([int]$h.loot_sent -gt 0) -and ($g.loot_shown -eq $h.loot_sent)) "sent=$($h.loot_sent) shown=$($g.loot_shown)"
Check "a pick-up removes it on the other PC" ((([int]$g.loot_taken -ge 1) -and ([int]$h.loot_removed -ge 1)) -or (([int]$h.loot_taken -ge 1) -and ([int]$g.loot_removed -ge 1))) "host took=$($h.loot_taken) guest removed=$($g.loot_removed) | guest took=$($g.loot_taken) host removed=$($h.loot_removed)"
Check "shared bag: the other got a copy" (([int]$h.loot_received -ge 1) -or ([int]$g.loot_received -ge 1)) "host received=$($h.loot_received) guest received=$($g.loot_received)"
Check "guest goes down instead of ending the run" ($sawDowned -and $sawHostSeesDowned) "guest down=$sawDowned host saw it=$sawHostSeesDowned"
Check "enemies also go for the guest" ([int]$h.target_swaps -gt 0) "times an enemy aimed at the guest=$($h.target_swaps)"
Check "host revives the guest" ([int]$g.revives -ge 1) "revives=$($g.revives)"
Check "both down: both go home" $bothHome "host=$($h.zone) guest=$($g.zone)"
$results | Format-Table -AutoSize | Out-String -Width 200
"SUITE: $(($results | Where-Object Result -eq 'PASS').Count)/$($results.Count) passed"
if (-not $KeepOpen) { Get-Process Moonlighter -ErrorAction SilentlyContinue | Stop-Process -Force }





