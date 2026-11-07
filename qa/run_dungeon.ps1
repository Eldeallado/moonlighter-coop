param([int]$Timeout = 260, [switch]$KeepOpen)
. "$PSScriptRoot\windows.ps1"
# Host enters the Golem dungeon, changes room twice and goes home; the guest must follow every step.
$ws = "E:\CoopAnything"
$hostDir = "$ws\test-host"; $guestDir = "$ws\test-guest"
$common = "-screen-fullscreen 0 -screen-width 800 -screen-height 450 -coopbot -coopscenario dungeon"

Get-Process Moonlighter -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep 2
Remove-Item "$hostDir\BepInEx\coop_*", "$guestDir\BepInEx\coop_*" -ErrorAction SilentlyContinue
$h = Start-Copy $hostDir "$common -coop host" 0
Start-Sleep 20
$g = Start-Copy $guestDir "$common -coop join 127.0.0.1" 820

function Read-Status($dir, $tag) {
    $f = "$dir\BepInEx\coop_${tag}_status.txt"; $o = @{}
    if (Test-Path $f) { try { Get-Content $f -ErrorAction Stop | ForEach-Object { $k, $v = $_ -split "=", 2; $o[$k] = $v } } catch {} }
    return $o
}

$seen = @{}; $sharedChanges = 0; $lastShared = ""   # zone pairs observed per scenario step
$sigMatch = $false; $roomsFollowed = @{}; $loadingStuck = 0; $lastLine = ""
$t0 = Get-Date
while (((Get-Date) - $t0).TotalSeconds -lt $Timeout) {
    $h = Read-Status $hostDir "host"; $g = Read-Status $guestDir "join"
    $step = [int]$h.scenario_step
    $line = "t={0,3:N0} step=$step host=$($h.zone) guest=$($g.zone) gload=$($g.loading_panel) sig=$($h.dungeon_sig)/$($g.dungeon_sig)" -f ((Get-Date) - $t0).TotalSeconds
    if ($line.Substring(6) -ne $lastLine) { $line; $lastLine = $line.Substring(6) }
    if ($h.zone -like "dungeon:*" -and $h.zone -eq $g.zone) { $roomsFollowed[$h.zone] = $true; if ($h.zone -ne $lastShared) { $sharedChanges++; $lastShared = $h.zone } }
    if ($h.dungeon_sig -and $h.dungeon_sig -eq $g.dungeon_sig) { $sigMatch = $true }
    if ($h.content_sig -and $g.content_sig) { if ($h.content_sig -eq $g.content_sig) { $contentMatch = $true } else { $contentDiff = "$($h.content_sig) vs $($g.content_sig)" } }
    if ($step -ge 4 -and $h.zone -eq "town" -and $g.zone -eq "town" -and $g.loading_panel -eq "False") { $doneTown = $true; break }
    Start-Sleep -Milliseconds 800
}

$results = @()
function Check($name, $ok, $detail) { $script:results += [pscustomobject]@{ Check = $name; Result = $(if ($ok) { "PASS" } else { "FAIL" }); Detail = $detail } }
$h = Read-Status $hostDir "host"; $g = Read-Status $guestDir "join"
Check "guest got the host's floor" ([int]$g.infos_received -ge 1) "sent=$($h.infos_sent) received=$($g.infos_received)"
Check "same floor layout" $sigMatch "signature matched at least once"
Check "same things inside the rooms" ([bool]$contentMatch) "match=$contentMatch last diff=$contentDiff seed=$($h.floor_seed)/$($g.floor_seed)"
Check "guest followed into 3 rooms" ($roomsFollowed.Count -ge 3) ("rooms shared: " + ($roomsFollowed.Keys -join " "))
Check "guest led through a door (host went too)" (([int]$g.door_requests_sent -ge 1) -and ([int]$h.door_requests_done -ge 1)) "asked=$($g.door_requests_sent) host_went=$($h.door_requests_done)"
Check "4+ room changes made together" ($sharedChanges -ge 4) "shared room changes: $sharedChanges"
Check "both back in town" ([bool]$doneTown) "host=$($h.zone) guest=$($g.zone)"
Check "no loading screen left on guest" ($g.loading_panel -eq "False") "loading_panel=$($g.loading_panel)"
$results | Format-Table -AutoSize | Out-String -Width 200
"SUITE: $(($results | Where-Object Result -eq 'PASS').Count)/$($results.Count) passed"
if (-not $KeepOpen) { Get-Process Moonlighter -ErrorAction SilentlyContinue | Stop-Process -Force }




