# Roles, on the hidden desktop: host Seeker, guest Guardian (then Fighter / Healer), into the Golem dungeon.
# Checks max health per role, the Seeker's extra chest loot, the secret-room marks on the map, and that
# everything goes back to normal in town.
$q = $PSScriptRoot
. "$q\oculto.ps1"
Get-Process Moonlighter -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep 2
foreach ($dd in "test-host", "test-guest") { Get-ChildItem "E:\CoopAnything\$dd\BepInEx" -Filter "coop_*" -File | ForEach-Object { $_.Delete() } }
$common = "-screen-fullscreen 0 -screen-width 640 -screen-height 360 -coopwin 640x360 -coopwindowed -coopdrive"
"host pid " + (Start-Oculto "E:\CoopAnything\test-host" "$common -coopbot -coopscenario none")
Start-Sleep 8
"guest pid " + (Start-Oculto "E:\CoopAnything\test-guest" "$common -coopdriveport 7801")
Start-Sleep 45
& "$q\drive.ps1" host "coop create" | Out-Null
Start-Sleep 4
& "$q\drive.ps1" host "coop listen" | Out-Null
Start-Sleep 1
& "$q\drive.ps1" guest "coop ip 127.0.0.1" | Out-Null
Start-Sleep 30
"guest: " + (& "$q\drive.ps1" guest "coop status")
$bits = [int]([regex]::Match((& "$q\drive.ps1" host "roleinfo"), 'rules=(\d+)').Groups[1].Value)
if ($bits % 2 -eq 0) { "roles on: " + (& "$q\drive.ps1" host "rule 0") }
foreach ($w in "host", "guest") { "$w town " + (& "$q\drive.ps1" $w hp) }
foreach ($w in "host", "guest") { & "$q\drive.ps1" $w "inv on" | Out-Null }
& "$q\drive.ps1" host "dungeon Golem" | Out-Null
Start-Sleep 30
"host " + (& "$q\drive.ps1" host "role 3")
"guest " + (& "$q\drive.ps1" guest "role 1")
Start-Sleep 2
foreach ($w in "host", "guest") { "$w dungeon " + (& "$q\drive.ps1" $w hp) }
for ($f = 1; $f -le 3; $f++) {
    $s = & "$q\drive.ps1" host "secrets"; "floor $f host (Seeker) $s / guest " + (& "$q\drive.ps1" guest "secrets")
    if ($s -notmatch 'secret rooms 0') { break }
    & "$q\drive.ps1" host "nextfloor" | Out-Null; Start-Sleep 25
}
$cr = & "$q\drive.ps1" host "chestroom"; "chest room $cr"
if ($cr -ne "none") { & "$q\drive.ps1" host "goroom $cr" | Out-Null; Start-Sleep 6 }
"host " + (& "$q\drive.ps1" host "openchest")
Start-Sleep 4
"host " + (& "$q\drive.ps1" host "openchest")
Start-Sleep 3
& "$q\drive.ps1" host "shot $env:TEMP\roles_chest.png" | Out-Null
"guest fighter " + (& "$q\drive.ps1" guest "role 2"); Start-Sleep 2; "guest " + (& "$q\drive.ps1" guest hp)
"guest healer " + (& "$q\drive.ps1" guest "role 4"); Start-Sleep 2; "guest " + (& "$q\drive.ps1" guest hp)
& "$q\drive.ps1" host "f2 off" | Out-Null
& "$q\drive.ps1" host "town" | Out-Null
Start-Sleep 30
foreach ($w in "host", "guest") { $i = & "$q\drive.ps1" $w info; "$w " + [regex]::Match($i, 'zone=\S+').Value + " " + (& "$q\drive.ps1" $w hp) }
