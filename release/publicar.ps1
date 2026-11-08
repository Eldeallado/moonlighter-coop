param([Parameter(Mandatory)][string]$Version, [Parameter(Mandatory)][string]$NotesFile)
# Builds the zip for $Version, pushes the code, creates the GitHub release and updates the game on this PC.
$ErrorActionPreference = "Continue"
$m = "E:\CoopAnything\MoonlighterCoop"; $r = "$m\release"; $gh = "C:\Program Files\GitHub CLI\gh.exe"
Get-Process Moonlighter -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep 2
Push-Location $m
dotnet build -c Release | Select-String "Compilación|error" | Select-Object -First 3
Copy-Item "$m\bin\Release\net472\MoonlighterCoop.dll" "$r\MoonlighterCoop\juego\BepInEx\plugins\" -Force
Copy-Item "$m\README.md" "$r\MoonlighterCoop\LEEME.md" -Force
Get-ChildItem $r -Filter "*.zip" | ForEach-Object { $_.Delete() }
$zip = "$r\MoonlighterCoop-$Version.zip"
Compress-Archive -Path "$r\MoonlighterCoop" -DestinationPath $zip -Force
Copy-Item "$m\bin\Release\net472\MoonlighterCoop.dll" "E:\Steam\steamapps\common\Moonlighter\BepInEx\plugins\" -Force
git add -A
git commit -q -m "Versión $Version"
git push -q origin main
& $gh release create "v$Version" $zip --repo Eldeallado/moonlighter-coop --title "Moonlighter Coop $Version" --notes-file $NotesFile
Pop-Location
