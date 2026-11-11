# Repetir solo las partidas por el adaptador UDP (tras cambiarlo). Resumen en resumen.txt.
param([string]$Out = "$env:TEMP\coop_barrido2")
$q = $PSScriptRoot
New-Item -ItemType Directory -Force $Out | Out-Null
$casos = @(
    @{ Nombre = "A1_adaptador";       Red = "0,40,40"; PerdidaPuente = 0.15 },
    @{ Nombre = "A2_adaptador_mala";  Red = "0,150,200"; PerdidaPuente = 0.25 },
    @{ Nombre = "A3_tres_adaptador";  Red = "0,40,40"; PerdidaPuente = 0.1; Jugadores = 3 },
    @{ Nombre = "A4_adaptador_otra";  Red = "0,40,40"; PerdidaPuente = 0.15 }
)
$res = Join-Path $Out "resumen.txt"
"Barrido adaptador $(Get-Date)" | Set-Content $res -Encoding UTF8
foreach ($c in $casos) {
    $a = @{ Nombre = $c.Nombre; Red = $c.Red; Out = $Out; Puente = $true; PerdidaPuente = $c.PerdidaPuente }
    if ($c.Jugadores) { $a.Jugadores = $c.Jugadores }
    & "$q\barrido.ps1" @a | Out-Null
    Add-Content $res "" -Encoding UTF8
    Get-Content (Join-Path (Join-Path $Out $c.Nombre) "informe.txt") -Encoding UTF8 | Where-Object { $_ -match '==|FALLO|excepciones|AVISO|red ' } | Add-Content $res -Encoding UTF8
}
Get-Content $res -Encoding UTF8
