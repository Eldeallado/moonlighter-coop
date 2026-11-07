# Moonlighter co-op: notas de trabajo

## Identidad del juego (2026-11-07)
- Steam app 606150, buildid 16760112, versión 1.15.8 (archivo `1.15.8.txt`), Windows.
- `Moonlighter.exe` SHA-256 `5F4832E220E928E7973155BAC2E08104C35EEB66FEB0D3EA2CF3827A590C6608`
- `Assembly-CSharp.dll` SHA-256 `D834F0AE78EAB04E2D741B30E352D515F1814AA2330C8ED684D7C59027E55CDC`
- Unity 2019.2.7, Mono, .NET 4.x. BepInEx 5.4.23.6 x64 (sha256 6c57a714…0755) funciona.

## Observado
- Steamworks.NET en Assembly-CSharp-firstpass. `SteamManager` llama `RestartAppIfNecessary(Invalid)`; con `steam_appid.txt` = 606150 junto al exe arranca directo.
- Guardado: Steam Cloud (`SteamCloudSaveController`, SteamRemoteStorage). Las dos copias de prueba comparten partida.
- `GameManager.LoadGameFromBytes(byte[])`, `SerializeGameSlot()`, `LoadSlot(GameSlot)`, `GetMostRecentSlot()`.
- Bot de arranque: en menú, `GUIManager.AllowPanelChange/DisableCurrentPanel/GoToDungeonLoadingPanel` + `GameManager.LoadSlot(slot)` → carga Town. (`MainMenuPanel.StartGame` no vale sin "pulsa una tecla".)
- Escenas: Managers (siempre), MainMenu, Town, Dungeon (aditivas).
- Mazmorras: `DungeonGenerator.GenerateDungeon` usa `UnityEngine.Random` global (no semilla propia). Existe `DungeonSaveInfo(DungeonManager)` + `DungeonGenerator.GenerateSavedDungeon(info)` (salas, conexiones, cofres con contenido, `choosenEnemyPack`, puertas). Serializable con JsonUtility.
- Will = `HeroMerchant(Clone)`: Animator `HeroMerchant` (params Walk, *Direction, ataques por arma, IsShopMode…), `HeroMerchantController`, `HeroMerchant` (singleton), `HeroMerchantInventory`, hijos `HeroMerchantArt/Will` (SpriteRenderer), `Weapon/ItemStack(x)/x(Clone)` con su Animator y SpriteRenderer.
- Anti-trampas CodeStage (ObscuredTypes) presente; no ha molestado a BepInEx.

## Plan (host autoritativo)
1. Transporte TCP (pruebas en un PC, VPN) + Steam P2P después.
2. Sesión: invitado recibe la partida del host (bytes) y la carga.
3. Muñeco del otro jugador: copiar sprites visibles (nombre de sprite, flip, orden) + posición.
4. Mazmorra: host manda DungeonSaveInfo, invitado la genera igual; misma sala; enemigos los decide el host.
5. Combate, botín, tienda.
