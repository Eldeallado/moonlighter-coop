# Opens the host and guest test copies side by side to play co-op on this PC (no bots).
. "$PSScriptRoot\qa\windows.ps1"
$ws = "E:\CoopAnything"
Get-Process Moonlighter -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep 1
$common = "-screen-fullscreen 0 -screen-width 800 -screen-height 450 -coopwindowed"
$h = Start-Copy "$ws\test-host" "$common -coop host" 0
Start-Sleep 3
$g = Start-Copy "$ws\test-guest" "$common -coop join 127.0.0.1" 820
# The jobs that place the windows live as long as this script.
Start-Sleep 70
