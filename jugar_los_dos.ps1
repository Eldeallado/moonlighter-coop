# Opens the host and guest test copies side by side to play co-op on this PC (no bots).
. "$PSScriptRoot\qa\windows.ps1"
$ws = "E:\CoopAnything"
Get-Process Moonlighter -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep 1
$common = "-screen-fullscreen 0 -screen-width 944 -screen-height 531 -coopwin 944x531 -coopwindowed"
$h = Start-Copy "$ws\test-host" "$common" -1920 40 960 570
Start-Sleep 3
$g = Start-Copy "$ws\test-guest" "$common" -960 40 960 570
# The jobs that place the windows live as long as this script.
Start-Sleep 70

