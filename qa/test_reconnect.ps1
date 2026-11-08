# Host + guest by IP, host in a dungeon kills an enemy, the guest's connection drops: it must come back to the same room.
$q = $PSScriptRoot
. "$q\windows.ps1"
function Info($who, $pattern) { $i = & "$q\drive.ps1" $who info; ([regex]::Matches($i, $pattern) | ForEach-Object { $_.Value }) -join " " }
foreach ($dd in "test-host", "test-guest") { Get-ChildItem "E:\CoopAnything\$dd\BepInEx" -Filter "coop_*" -File | ForEach-Object { $_.Delete() } }
$common = "-screen-fullscreen 0 -screen-width 640 -screen-height 360 -coopwin 640x360 -coopwindowed -coopdrive"
Start-Copy "E:\CoopAnything\test-host" "$common -coopbot -coopscenario none" -1920 0 656 399 | Out-Null
Start-Sleep 8
Start-Copy "E:\CoopAnything\test-guest" "$common -coopdriveport 7801" -1280 0 656 399 | Out-Null
Start-Sleep 45
& "$q\drive.ps1" host "coop create" | Out-Null
& "$q\drive.ps1" host "coop listen" | Out-Null
Start-Sleep 2
& "$q\drive.ps1" guest "coop ip 127.0.0.1" | Out-Null
Start-Sleep 30
foreach ($w in "host", "guest") { & "$q\drive.ps1" $w "inv on" | Out-Null }
& "$q\drive.ps1" host "dungeon Golem" | Out-Null
Start-Sleep 30
$f = & "$q\drive.ps1" host "findenemy Slime"
$m = [regex]::Match($f, '(\d+),(\d+):')
& "$q\drive.ps1" host "goroom $($m.Groups[1].Value) $($m.Groups[2].Value)" | Out-Null
Start-Sleep 12
"host before : " + (Info host 'zone=\S+|enemies=\[[^\]]*\]')
"kill        : " + (& "$q\drive.ps1" host "killone")
Start-Sleep 3
"guest before: " + (Info guest 'zone=\S+|enemies=\[[^\]]*\]')
"--- guest connection drops"
& "$q\drive.ps1" guest "dropnet" | Out-Null
Start-Sleep 75
"guest after : " + (Info guest 'zone=\S+|enemies=\[[^\]]*\]') + " | " + (& "$q\drive.ps1" guest "coop status")
"host after  : " + (& "$q\drive.ps1" host "coop status")
Select-String "E:\CoopAnything\test-guest\BepInEx\LogOutput.log" -Pattern 'reconnect|removed|guest builds' | ForEach-Object Line
Select-String "E:\CoopAnything\test-host\BepInEx\LogOutput.log" -Pattern 'catch-up|player . is gone' | ForEach-Object Line
