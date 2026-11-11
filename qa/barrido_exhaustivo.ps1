# Revisión a fondo (unas 2 h y media): 4 jugadores, redes raras (mucho retraso, solo pérdidas, solo variación)
# y repeticiones de todo. Resumen en resumen.txt; cada partida con vídeos e informe en su carpeta.
param([string]$Out = "$env:TEMP\coop_exhaustivo")
$q = $PSScriptRoot
New-Item -ItemType Directory -Force $Out | Out-Null
$casos = @(
    @{ Nombre = "01_cuatro_perfecta";   Red = "0,0,0";        Jugadores = 4 },
    @{ Nombre = "02_cuatro_wifi";       Red = "0.03,30,40";   Jugadores = 4 },
    @{ Nombre = "03_cuatro_moviles";    Red = "0.08,90,120";  Jugadores = 4 },
    @{ Nombre = "04_cuatro_mala";       Red = "0.2,200,250";  Jugadores = 4 },
    @{ Nombre = "05_cuatro_adaptador";  Red = "0,40,40";      Jugadores = 4; Puente = $true; PerdidaPuente = 0.1 },
    @{ Nombre = "06_solo_perdidas";     Red = "0.3,0,0" },
    @{ Nombre = "07_mucho_retraso";     Red = "0,400,50" },
    @{ Nombre = "08_mucha_variacion";   Red = "0,20,500" },
    @{ Nombre = "09_tres_solo_perdidas"; Red = "0.3,0,0";     Jugadores = 3 },
    @{ Nombre = "10_tres_mucho_retraso"; Red = "0,400,50";    Jugadores = 3 },
    @{ Nombre = "11_adaptador_moviles"; Red = "0.05,90,120";  Puente = $true; PerdidaPuente = 0.1 },
    @{ Nombre = "12_tres_mala_a";       Red = "0.2,200,250";  Jugadores = 3 },
    @{ Nombre = "13_tres_mala_b";       Red = "0.2,200,250";  Jugadores = 3 },
    @{ Nombre = "14_tres_mala_c";       Red = "0.2,200,250";  Jugadores = 3 },
    @{ Nombre = "15_dos_mala_a";        Red = "0.2,200,250" },
    @{ Nombre = "16_dos_mala_b";        Red = "0.2,200,250" },
    @{ Nombre = "17_tres_moviles";      Red = "0.08,90,120";  Jugadores = 3 },
    @{ Nombre = "18_tres_adaptador";    Red = "0,40,40";      Jugadores = 3; Puente = $true; PerdidaPuente = 0.15 },
    @{ Nombre = "19_perfecta";          Red = "0,0,0" },
    @{ Nombre = "20_wifi";              Red = "0.03,30,40" }
)
$res = Join-Path $Out "resumen.txt"
"Revisión a fondo $(Get-Date)" | Set-Content $res -Encoding UTF8
foreach ($c in $casos) {
    $a = @{ Nombre = $c.Nombre; Red = $c.Red; Out = $Out }
    if ($c.Jugadores) { $a.Jugadores = $c.Jugadores }
    if ($c.Puente) { $a.Puente = $true; $a.PerdidaPuente = $c.PerdidaPuente }
    & "$q\barrido.ps1" @a | Out-Null
    $inf = Join-Path (Join-Path $Out $c.Nombre) "informe.txt"
    Add-Content $res "" -Encoding UTF8
    Get-Content $inf -Encoding UTF8 | Where-Object { $_ -match '==|FALLO|AVISO' } | Add-Content $res -Encoding UTF8
}
Get-Content $res -Encoding UTF8
