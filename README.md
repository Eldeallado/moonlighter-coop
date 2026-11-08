# Moonlighter Coop

**Cooperativo online para Moonlighter (hasta 5 jugadores).** Un mod no oficial que convierte la aventura de un jugador de Moonlighter en una partida compartida: bajáis juntos a las mazmorras, peleáis contra los mismos enemigos, compartís botín, dinero y tienda, y os revivís al estilo *The Binding of Isaac*.

> Mod hecho por fans, sin relación con Digital Sun ni 11 bit studios. Necesitas tener **Moonlighter** comprado en Steam. Este repositorio no contiene ningún archivo del juego.

*English: unofficial online co-op mod for Moonlighter (Steam, Windows), up to 5 players. Download the zip from Releases, run `Instalar.bat`, press F2 in game to host or join with a code.*

---

## Descarga e instalación

1. Ve a **[Releases](../../releases/latest)** y descarga `MoonlighterCoop-x.y.z.zip`.
2. Descomprímelo donde quieras.
3. Cierra Moonlighter y abre **`Instalar.bat`**. Busca solo tu Moonlighter de Steam y copia el mod.
4. Abre Moonlighter desde Steam como siempre.

Para quitarlo: **`Desinstalar.bat`** deja el juego como estaba. Tus partidas no se tocan en ningún caso.

Todos los jugadores necesitan el mod instalado (la misma versión).

## Cómo jugar

| Tecla | Qué hace |
|---|---|
| **F2** | Menú cooperativo: crear partida, unirse con un código o por IP, ver quién está, expulsar, salir |
| **F3** | Elegir el color del cuerpo y del pelo de tu Will (barras de tono, intensidad y brillo, con vista previa) |

1. **Quien hace de anfitrión** carga su partida, pulsa **F2 → Crear partida** y le sale un **código de 7 letras** (también puede invitar a amigos de Steam desde ahí).
2. **Los demás** pulsan **F2 → Unirse con un código** y lo escriben. Entran solos en la partida del anfitrión.
3. ¿Misma casa o red? El anfitrión puede activar **Permitir unirse por IP** y los demás usan **Unirse por IP**.

Se juega con la partida del anfitrión; la de cada invitado no se modifica.

## Características

**Conexión**
- Online por Steam (sin abrir puertos) con código de partida, o por IP en red local / VPN.
- Hasta 5 jugadores. Cada uno tiene su color y su nombre.
- Vida de los compañeros arriba a la izquierda, debajo de la tuya.
- El anfitrión puede expulsar a un jugador (no puede volver a entrar en esa sesión).

**Mazmorras**
- Misma mazmorra para todos: mismas salas, decorado, cofres y enemigos (generadas con la misma semilla).
- Salas como en Isaac: si uno cruza una puerta, pasáis todos.
- Enemigos compartidos: los mata cualquiera, mueren en todas las pantallas y van a por el jugador vivo más cercano.
- Botín compartido: sale en el mismo sitio para todos y lo que recoge uno llega a la mochila común.
- Cajas, vasijas y hierbas se rompen para todos.

**Morir y revivir**
- Al morir te conviertes en **fantasma**: vuelas, no puedes atacar ni recoger y los enemigos te ignoran.
- Revives cuando un compañero vivo **entra en una fuente** o cuando **bajáis al siguiente piso**.
- Si caéis todos, fin de la expedición y **votación**: "jugar de nuevo" o "ir a la villa" (gana la mayoría; empate, decide el anfitrión).

**Pueblo y tienda**
- Pueblo y tienda juntos: si uno entra o sale de la tienda, entráis o salís todos.
- Dinero y mochila compartidos (el equipo que lleva puesto cada uno es suyo).
- Tienda compartida: estanterías y precios iguales para todos, los clientes se ven en todas las pantallas, cualquiera puede cobrar en la caja y atrapar ladrones rodando contra ellos.
- El día y la noche van a la vez para todos, y el resumen del día es el mismo.

**Estilo**
- Avisos en pergamino con la letra del propio juego, menús propios para el cooperativo y los colores.

## Seguridad

- Por Steam la partida es **invisible**: no sale en ninguna lista y solo se puede entrar con el código.
- **No se abre ningún puerto** en tu PC salvo que el anfitrión active "Permitir unirse por IP" (y se cierra al terminar).
- Al unirte, la partida que manda el anfitrión **se revisa antes de cargarla**: se rechaza si es demasiado grande o pide crear tipos de datos que no son del juego.
- Aun así, como en cualquier juego online con mods: **juega solo con gente de confianza** y pasa el código solo a quien quieras que entre.
- El mod no se conecta a ningún servidor propio, no recoge datos ni toca tu cuenta: solo usa la red de Steam o la conexión directa entre jugadores.

## Compatibilidad

- Moonlighter de **Steam para Windows**, versión **1.15.8** (el instalador avisa si tu versión es otra).
- Probado con el juego base. El DLC *Between Dimensions* no se ha probado en cooperativo.
- Usa [BepInEx 5](https://github.com/BepInEx/BepInEx) (incluido en el zip).

## Limitaciones conocidas

- Los ladrones y los clientes los "controla" el anfitrión; en los invitados son copias (se ven y se pueden atrapar y cobrar, pero si hay mucho retraso pueden ir un poco por detrás).
- Algunos enemigos que nacen de otros (por ejemplo, slimes que se dividen) se sincronizan por posición y puede haber pequeñas diferencias.
- Probado a fondo en un mismo PC y por Steam; con mucha distancia entre jugadores puede notarse el retraso.

## Compilar desde el código

Necesitas .NET SDK y una copia de Moonlighter con BepInEx. En `MoonlighterCoop.csproj`, cambia `GamePath` a tu carpeta de Moonlighter y ejecuta:

```
dotnet build -c Release
```

La DLL queda en `bin/Release/net472/MoonlighterCoop.dll`; cópiala a `BepInEx/plugins` del juego.

## Créditos

- Mod: **elde_al_lado**, siguiendo el método [Co-op Anything](https://github.com/cranebeaker/coop-anything).
- [BepInEx](https://github.com/BepInEx/BepInEx) (LGPL-2.1) y [HarmonyX](https://github.com/BepInEx/HarmonyX) (MIT) para cargar y parchear el juego.
- Moonlighter © Digital Sun / 11 bit studios. Todas las marcas pertenecen a sus dueños.

## Licencia

El código de este mod está bajo licencia [MIT](LICENSE). BepInEx y sus componentes, incluidos en el zip de descarga, mantienen sus propias licencias.
