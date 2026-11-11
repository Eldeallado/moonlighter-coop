# Todas las combinaciones de red y jugadores, una detrás de otra (unos 50 minutos). Resumen en resumen.txt.
param([string]$Out = "$env:TEMP\coop_barrido")
$q = $PSScriptRoot
New-Item -ItemType Directory -Force $Out | Out-Null
$casos = @(
    @{ Nombre = "1_perfecta";      Red = "0,0,0" },
    @{ Nombre = "2_wifi";          Red = "0.03,30,40" },
    @{ Nombre = "3_datos_moviles"; Red = "0.08,90,120" },
    @{ Nombre = "4_red_mala";      Red = "0.2,200,250" },
    @{ Nombre = "5_adaptador_udp"; Red = "0,40,40"; Puente = $true; PerdidaPuente = 0.15 },
    @{ Nombre = "6_tres_wifi";     Red = "0.03,30,40"; Jugadores = 3 },
    @{ Nombre = "7_tres_mala";     Red = "0.2,200,250"; Jugadores = 3 },
    @{ Nombre = "8_adaptador_mala"; Red = "0,150,200"; Puente = $true; PerdidaPuente = 0.25 },
    @{ Nombre = "9_tres_moviles";  Red = "0.08,90,120"; Jugadores = 3 },
    @{ Nombre = "10_tres_adaptador"; Red = "0,40,40"; Jugadores = 3; Puente = $true; PerdidaPuente = 0.1 },
    # Repeticiones: los fallos de red no siempre salen a la primera
    @{ Nombre = "11_perfecta_otra"; Red = "0,0,0" },
    @{ Nombre = "12_wifi_otra";    Red = "0.03,30,40" },
    @{ Nombre = "13_moviles_otra"; Red = "0.08,90,120" },
    @{ Nombre = "14_mala_otra";    Red = "0.2,200,250" },
    @{ Nombre = "15_adaptador_otra"; Red = "0,40,40"; Puente = $true; PerdidaPuente = 0.15 }
)
$res = Join-Path $Out "resumen.txt"
"Barrido $(Get-Date)" | Set-Content $res -Encoding UTF8
foreach ($c in $casos) {
    $a = @{ Nombre = $c.Nombre; Red = $c.Red; Out = $Out }
    if ($c.Jugadores) { $a.Jugadores = $c.Jugadores }
    if ($c.Puente) { $a.Puente = $true; $a.PerdidaPuente = $c.PerdidaPuente }
    & "$q\barrido.ps1" @a | Out-Null
    $inf = Join-Path (Join-Path $Out $c.Nombre) "informe.txt"
    Add-Content $res "" -Encoding UTF8
    Get-Content $inf -Encoding UTF8 | Where-Object { $_ -match '==|FALLO|excepciones|AVISO' } | Add-Content $res -Encoding UTF8
}
Get-Content $res -Encoding UTF8
