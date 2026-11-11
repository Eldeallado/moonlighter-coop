# Barrido de pruebas: una partida entera grabada en vídeo, con la red simulada que se pida, comprobando en
# cada paso que los juegos coinciden. Deja en $Out: vídeos (mp4), registros de cada copia e informe.txt.
#   -Red "perdida,retardo,variacion"  (p. ej. "0.08,90,120" = datos móviles)
#   -Jugadores 2|3   -Puente (los invitados entran por el adaptador UDP, con -PerdidaPuente)
param([string]$Nombre = "prueba", [string]$Red = "0,0,0", [int]$Jugadores = 2, [switch]$Puente, [double]$PerdidaPuente = 0.1,
      [string]$Out = "$env:TEMP\coop_barrido")
$q = $PSScriptRoot
. "$q\oculto.ps1"
$Out = Join-Path $Out $Nombre
if (Test-Path $Out) { Remove-Item -LiteralPath $Out -Recurse -Force }
New-Item -ItemType Directory -Force $Out | Out-Null
$inf = Join-Path $Out "informe.txt"
$quien = @("host", "guest", "guest2")[0..($Jugadores - 1)]
$dirs = @{ host = "test-host"; guest = "test-guest"; guest2 = "test-guest2" }
$fallos = 0
$t0 = Get-Date
function Nota($s) { $l = "[{0,6:N0} s] {1}" -f ((Get-Date) - $t0).TotalSeconds, $s; $l; Add-Content $inf $l -Encoding UTF8 }
function Ok($cond, $que) { if ($cond) { Nota "OK    $que" } else { $script:fallos++; Nota "FALLO $que" } }
function D($w, $c) { try { (& "$q\drive.ps1" $w $c) -join " " } catch { "ERROR driver: $($_.Exception.Message)" } }
function Info($w) {
    $s = D $w "info"; $o = @{ raw = $s }
    foreach ($k in "zone", "pos", "hp", "gold", "ghost", "room") { if ($s -match " $k=(\S+)" -or $s -match "^$k=(\S+)") { $o[$k] = $Matches[1] } }
    if ($s -match " bag=\[([^\]]*)\]") { $o.bag = $Matches[1] }
    if ($s -match " enemies=\[([^\]]*)\]") { $o.enemies = $Matches[1] }
    return $o
}
function Esperar($seg) { Start-Sleep $seg }
function MismaSala($que) {
    $z = $quien | ForEach-Object { (Info $_).zone }
    Ok (($z | Select-Object -Unique).Count -eq 1) "$que : misma sala ($($z -join ' | '))"
}

Get-Process Moonlighter -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep 2
foreach ($w in $quien) { Get-ChildItem "E:\CoopAnything\$($dirs[$w])\BepInEx" -Filter "coop_*" -File -ErrorAction SilentlyContinue | ForEach-Object { $_.Delete() } }
$common = "-screen-fullscreen 0 -screen-width 1280 -screen-height 720 -coopwin 1280x720 -coopwindowed -coopdrive"
Nota "== $Nombre : red $Red, $Jugadores jugadores$(if ($Puente) { ", por el adaptador UDP (pérdida $PerdidaPuente)" })"
[void](Start-Oculto "E:\CoopAnything\test-host" "$common -coopbot -coopscenario none")
Start-Sleep 8
for ($i = 1; $i -lt $Jugadores; $i++) { [void](Start-Oculto "E:\CoopAnything\$($dirs[$quien[$i]])" "$common -coopdriveport $(7800 + $i)"); Start-Sleep 6 }
Start-Sleep 40

# Grabar a todos y poner la red simulada
foreach ($w in $quien) { [void](D $w "grabar $Out\frames_$w 5") }
$p = $Red -split ","
foreach ($w in $quien) { [void](D $w "red $($p[0]) $($p[1]) $($p[2])") }

# 1. Conectar
[void](D host "coop create"); Esperar 8
if ($Puente) {
    foreach ($w in $quien) { [void](D $w "perdida $PerdidaPuente") }
    $code = (D host "puenteprueba").Trim()
    Nota "código de prueba $code"
    foreach ($w in $quien[1..($quien.Count - 1)]) { [void](D $w "coop code $code"); Esperar 3 }
} else {
    foreach ($w in $quien[1..($quien.Count - 1)]) { [void](D $w "coop ip 127.0.0.1"); Esperar 3 }
}
Esperar 40
foreach ($w in $quien[1..($quien.Count - 1)]) { $s = D $w "coop status"; Ok ($s -match "connected=True") "conecta $w ($s)" }
Ok ((D host "coop status") -match "players=$Jugadores") "el anfitrión ve $Jugadores jugadores"

# 2. Mazmorra
# Reglas neutras: sin roles (su panel para la partida), sin "un solo corazón", sin "revivir difícil" y sin carrera
$bits = [int]([regex]::Match((D host "roleinfo"), 'rules=(\d+)').Groups[1].Value)
foreach ($n in 0, 3, 5, 6) { if (($bits -band (1 -shl $n)) -ne 0) { [void](D host "rule $n") } }
Nota "reglas: $(D host 'roleinfo')"
foreach ($w in $quien) { [void](D $w "inv on") }
[void](D host "gold 3000")
[void](D host "dungeon Golem"); Esperar 40
MismaSala "entrar en la mazmorra"
# Al cargar la mazmorra se pierde el invencible de pruebas: otra vez (si no, el anfitrión muere peleando)
foreach ($w in $quien) { [void](D $w "inv on") }

# 3. Cambiar de sala (estilo Isaac: pasan todos)
$i = Info host
$m = [regex]::Match((D host "info"), 'LevelDoor(\w+)@[^;]*?->(\d+,\d+)(?!\(closed\))')
if ($m.Success) { [void](D host "door $($m.Groups[1].Value)"); Esperar 10; MismaSala "cruzar una puerta" } else { Nota "AVISO sin puerta abierta para cruzar" }

# 4. Pelear un rato todos
& "$q\fight.ps1" -Who $quien -Rounds 10 | Out-Null
# Un enemigo que solo le queda a un invitado se quita a los 3 s (EnemySync.Cull)
Esperar 5
$e = $quien | ForEach-Object { $x = (Info $_).enemies; if ($x) { ($x -split ";").Count } else { 0 } }
Ok (($e | Select-Object -Unique).Count -eq 1) "mismos enemigos vivos tras pelear ($($e -join ' | '))"

# 5. Oro y mochila compartidos
Esperar 4
$g = $quien | ForEach-Object { (Info $_).gold }
Ok (($g | Select-Object -Unique).Count -eq 1) "mismo oro ($($g -join ' | '))"
$b = $quien | ForEach-Object { (Info $_).bag }
Ok (($b | Select-Object -Unique).Count -eq 1) "misma mochila ($($b -join ' | '))"

# 6. Muere el primer invitado: fantasma, los demás lo ven, se mueve
[void](D guest "inv off"); [void](D guest "hurt 9999"); Esperar 10
Ok ((D guest "posesize") -match "fantasma=True") "el invitado muerto es fantasma"
Ok ((D host "posesize") -match "fantasmas=.*1") "el anfitrión ve al invitado como fantasma"
$a = (Info guest).pos; [void](D guest "stick 1 0"); Esperar 2; [void](D guest "stop"); Esperar 1; $c = (Info guest).pos
# Pegado a la pared derecha no avanza: hacia el otro lado
if ($a -eq $c) { [void](D guest "stick -1 0"); Esperar 2; [void](D guest "stop"); Esperar 1; $c = (Info guest).pos }
Ok ($a -ne $c) "el fantasma se mueve ($a -> $c)"
Nota ("tamaño de poses: " + ($quien | ForEach-Object { "$_ " + (D $_ "posesize") }) -join " | ")

# 7. Siguiente piso: el fantasma revive
[void](D host "nextfloor"); Esperar 45
MismaSala "bajar de piso"
$b = $quien | ForEach-Object { (Info $_).bag }
Ok (($b | Select-Object -Unique).Count -eq 1) "misma mochila al bajar de piso ($($b -join ' | '))"
Ok ((D guest "posesize") -match "fantasma=False") "el fantasma revive al bajar de piso"
foreach ($w in $quien) { [void](D $w "inv on") }

# 7b. Caen todos: todos tienen que ver la misma pantalla final (antes el anfitrión fantasma veía "Escapó con el amuleto")
foreach ($w in $quien) { [void](D $w "inv off") }
[void](D guest "hurt 9999"); Esperar 8
foreach ($w in $quien | Where-Object { $_ -ne "guest" }) { [void](D $w "hurt 9999"); Esperar 8 }
Esperar 6
$pan = $quien | ForEach-Object { if ((D $_ "info") -match " panel=(\S+)") { $Matches[1] } else { "?" } }
Ok ((($pan | Select-Object -Unique).Count -eq 1) -and $pan[0] -ne "none") "caen todos: misma pantalla final ($($pan -join ' | '))"
foreach ($w in $quien) { [void](D $w "shot $Out\fin_$w.png") }
# Votan todos ir a la villa (como harían los jugadores en la pantalla final)
foreach ($w in $quien) { [void](D $w "votar village") }; Esperar 30
$z = $quien | ForEach-Object { (Info $_).zone }
Ok (($z | Where-Object { $_ -notmatch "^dungeon" }).Count -eq $quien.Count) "tras votar, todos al pueblo ($($z -join ' | '))"
Esperar 2

# 8. Al pueblo
[void](D host "village"); Esperar 40
$z = $quien | ForEach-Object { (Info $_).zone }
Ok (($z | Where-Object { $_ -notmatch "^dungeon" }).Count -eq $quien.Count) "todos vuelven al pueblo ($($z -join ' | '))"

# 9. Tienda
[void](D host "shop"); Esperar 25
$z = $quien | ForEach-Object { (Info $_).zone }
Ok (($z | Select-Object -Unique).Count -eq 1) "entran todos en la tienda ($($z -join ' | '))"

# Fin: red, grabación, registros
foreach ($w in $quien) { Nota "red $w : $(D $w 'red')" }
foreach ($w in $quien) { [void](D $w "grabar off") }
Esperar 3
Get-Process Moonlighter -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep 2
foreach ($w in $quien) {
    $log = "E:\CoopAnything\$($dirs[$w])\BepInEx\LogOutput.log"
    Copy-Item $log "$Out\registro_$w.log" -Force
    $exc = Select-String -Path $log -Pattern 'Exception' | Where-Object { $_.Line -notmatch 'Steamworks is not initialized' }
    $grp = $exc | ForEach-Object { ($_.Line -replace '\d+', '#').Substring(0, [Math]::Min(140, $_.Line.Length)) } | Group-Object | Sort-Object Count -Descending
    Nota "registro $w : $($exc.Count) excepciones"
    $grp | Select-Object -First 6 | ForEach-Object { Nota ("   {0}x {1}" -f $_.Count, $_.Name) }
    $f = Get-ChildItem "$Out\frames_$w" -Filter *.jpg -ErrorAction SilentlyContinue
    if ($f.Count -gt 0) {
        & ffmpeg -loglevel error -y -framerate 5 -i "$Out\frames_$w\f%05d.jpg" -c:v libx264 -pix_fmt yuv420p "$Out\video_$w.mp4"
        Nota "vídeo $w : $($f.Count) fotogramas"
    }
}
Nota "== $Nombre : $fallos fallos"
