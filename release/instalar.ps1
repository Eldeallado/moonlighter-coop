param([switch]$Desinstalar)
# Instala (o quita) Moonlighter Coop en tu Moonlighter de Steam. No toca tus partidas.
$ErrorActionPreference = "Stop"
$here = Split-Path -Parent $MyInvocation.MyCommand.Path

function Find-Moonlighter {
    $steam = (Get-ItemProperty "HKCU:\Software\Valve\Steam" -ErrorAction SilentlyContinue).SteamPath
    $libs = @()
    if ($steam) {
        $libs += ($steam -replace "/", "\")
        $vdf = Join-Path $libs[0] "steamapps\libraryfolders.vdf"
        if (Test-Path $vdf) { Select-String -Path $vdf -Pattern '"path"\s+"([^"]+)"' | ForEach-Object { $libs += ($_.Matches[0].Groups[1].Value -replace "\\\\", "\") } }
    }
    foreach ($l in ($libs | Select-Object -Unique)) {
        $p = Join-Path $l "steamapps\common\Moonlighter"
        if (Test-Path (Join-Path $p "Moonlighter.exe")) { return $p }
    }
    return $null
}

$game = Find-Moonlighter
if (-not $game) { Write-Host "No encuentro Moonlighter en Steam. ¿Está instalado?" -ForegroundColor Red; Read-Host "Pulsa Enter para salir"; exit 1 }
Write-Host "Moonlighter encontrado en: $game"
if (Get-Process Moonlighter -ErrorAction SilentlyContinue) { Write-Host "Cierra Moonlighter primero." -ForegroundColor Yellow; Read-Host "Pulsa Enter para salir"; exit 1 }

$files = @("winhttp.dll", "doorstop_config.ini", ".doorstop_version", "changelog.txt")
$marker = Join-Path $game "BepInEx\.moonlighter_coop"
if ($Desinstalar) {
    if (-not (Test-Path $marker)) { Write-Host "El mod no estaba instalado aquí." }
    foreach ($f in $files) { $p = Join-Path $game $f; if (Test-Path $p) { Remove-Item $p -Force } }
    if (Test-Path (Join-Path $game "BepInEx")) { Remove-Item (Join-Path $game "BepInEx") -Recurse -Force }
    Write-Host "Moonlighter Coop desinstalado. El juego queda como estaba." -ForegroundColor Green
    Read-Host "Pulsa Enter para salir"; exit 0
}

# Same game build the mod was made for?
$expected = "5F4832E220E928E7973155BAC2E08104C35EEB66FEB0D3EA2CF3827A590C6608"
$actual = (Get-FileHash (Join-Path $game "Moonlighter.exe")).Hash
if ($actual -ne $expected) { Write-Host "Aviso: tu Moonlighter es otra versión ($actual). Puede que el mod no funcione." -ForegroundColor Yellow }

Copy-Item (Join-Path $here "juego\*") $game -Recurse -Force
New-Item -ItemType File -Force $marker | Out-Null
Write-Host ""
Write-Host "Instalado. Cómo jugar:" -ForegroundColor Green
Write-Host "  1. Abre Moonlighter desde Steam (los dos con el mod)."
Write-Host "  2. El anfitrión carga su partida, pulsa F2 y elige Crear partida: le sale un código."
Write-Host "  3. El amigo pulsa F2, Unirse con un código, y escribe ese código (hasta 5 jugadores)."
Write-Host "  4. F3 abre el menú para elegir el color del cuerpo y del pelo de tu Will."
Read-Host "Pulsa Enter para salir"
