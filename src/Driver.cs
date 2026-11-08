using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using HarmonyLib;
using InControl;
using UnityEngine;

namespace MoonlighterCoop
{
    /// QA remote control (-coopdrive): a virtual gamepad fed into Will's real input actions, plus a line-based
    /// command port on 127.0.0.1 (7800 host, 7801 guest). Will walks, attacks and rolls as with a real pad.
    public class Driver : MonoBehaviour
    {
        public static Driver Instance;
        public static int Injected;
        public static readonly Dictionary<OneAxisInputControl, float> Pad = new Dictionary<OneAxisInputControl, float>();
        private static readonly ConcurrentQueue<(string cmd, Action<string> reply)> _queue = new ConcurrentQueue<(string, Action<string>)>();
        private Vector2 _stick;
        private Vector2? _walkTarget;
        private readonly Queue<Vector2> _path = new Queue<Vector2>();
        private Vector2 _pushDir; private float _pushUntil;
        private float _walkUntil;
        private readonly Dictionary<string, float> _buttonsUntil = new Dictionary<string, float>();
        private readonly Dictionary<string, float> _guiUntil = new Dictionary<string, float>();

        public static void Start(int port)
        {
            var go = new GameObject("CoopDriver");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<Driver>();
            var l = new TcpListener(IPAddress.Loopback, port);
            l.Start();
            new Thread(() => AcceptLoop(l)) { IsBackground = true }.Start();
            Plugin.Log.LogInfo($"[drive] remote control on 127.0.0.1:{port}");
        }

        private static void AcceptLoop(TcpListener l)
        {
            while (true)
            {
                TcpClient c;
                try { c = l.AcceptTcpClient(); } catch { return; }
                new Thread(() =>
                {
                    try
                    {
                        using (c)
                        {
                            var s = c.GetStream();
                            var rd = new StreamReader(s, Encoding.UTF8);
                            var wr = new StreamWriter(s, new UTF8Encoding(false)) { AutoFlush = true };
                            string line;
                            while ((line = rd.ReadLine()) != null)
                            {
                                var done = new ManualResetEventSlim();
                                string answer = "";
                                _queue.Enqueue((line, a => { answer = a; done.Set(); }));
                                done.Wait(10000);
                                wr.WriteLine(answer.Replace("\n", "\\n"));
                            }
                        }
                    }
                    catch { }
                }) { IsBackground = true }.Start();
            }
        }

        private HeroMerchantActions Actions => HeroMerchant.Instance?.heroMerchantController?.inputDevice;

        private void Update()
        {
            // Both copies run side by side and only one has focus: keep reading input in the background.
            if (InputManager.SuspendInBackground) InputManager.SuspendInBackground = false;
            Traverse.Create(typeof(InputManager)).Field("applicationIsFocused").SetValue(true);
            while (_queue.TryDequeue(out var q))
            {
                string a;
                try { a = Run(q.cmd.Trim()); } catch (Exception e) { a = "error " + e.Message; }
                q.reply(a);
            }
            var hero = HeroMerchant.Instance;
            var act = Actions;
            Pad.Clear();
            // Menus and panels (results screen, shop...) read the GUI actions, not Will's.
            var gin = GUIManager.Instance != null ? (object)GUIManager.Instance.input : null;
            foreach (var kv in _guiUntil.ToList())
            {
                if (Time.unscaledTime > kv.Value) { _guiUntil.Remove(kv.Key); continue; }
                var gb = gin != null ? Traverse.Create(gin).Field(kv.Key).GetValue<PlayerAction>() : null;
                if (gb != null) Pad[gb] = 1f;
            }
            if (hero == null || act == null) return;
            var stick = _stick;
            // Loading a save from the menu by script skips the wake-up that normally allows movement.
            if ((_walkTarget.HasValue || stick != Vector2.zero) && !hero.heroMerchantController.canMove && !Downed.Me && GameManager.Instance.NotLoadingScene && !(bool)hero.heroMerchantStats.isDead && GUIManager.Instance.currentInventory == null) hero.heroMerchantController.AllowMovement();
            if (!_walkTarget.HasValue && _path.Count > 0) { _walkTarget = _path.Dequeue(); _walkUntil = Time.unscaledTime + 4f; }
            if (!_walkTarget.HasValue && _path.Count == 0 && Time.unscaledTime < _pushUntil) stick = _pushDir;
            if (_walkTarget.HasValue)
            {
                var d = _walkTarget.Value - (Vector2)hero.transform.position;
                if (d.magnitude < 6f || Time.unscaledTime > _walkUntil)
                {
                    _walkTarget = null; stick = Vector2.zero;
                    if (_path.Count == 0 && _pushDir != Vector2.zero && _pushUntil < 0f) _pushUntil = Time.unscaledTime + 1.8f;
                }
                else stick = d.normalized;
            }
            if (stick.x > 0.2f) Pad[act.LeftStickRight] = stick.x;
            if (stick.x < -0.2f) Pad[act.LeftStickLeft] = -stick.x;
            if (stick.y > 0.2f) Pad[act.LeftStickUp] = stick.y;
            if (stick.y < -0.2f) Pad[act.LeftStickDown] = -stick.y;
            foreach (var kv in _buttonsUntil.ToList())
            {
                if (Time.unscaledTime > kv.Value) { _buttonsUntil.Remove(kv.Key); continue; }
                var b = Traverse.Create(act).Field(kv.Key).GetValue<PlayerAction>();
                if (b != null) Pad[b] = 1f;
            }
        }

        // Walk around holes and walls with the game's own navigation map of the current room.
        private void PathTo(Vector2 target, Vector2 pushAfter)
        {
            _path.Clear(); _walkTarget = null; _stick = Vector2.zero;
            _pushDir = pushAfter; _pushUntil = pushAfter == Vector2.zero ? 0f : -1f;
            var nav = DungeonManager.Instance != null ? DungeonManager.Instance.polyNavInstance : null;
            if (nav == null) nav = UnityEngine.Object.FindObjectOfType<PolyNav2D>();
            var start = (Vector2)HeroMerchant.Instance.transform.position;
            if (nav == null) { _path.Enqueue(target); return; }
            var end = nav.PointIsValid(target) ? target : nav.GetCloserEdgePoint(target);
            nav.FindPath(start, end, pts =>
            {
                if (pts == null || pts.Length == 0) { _path.Enqueue(target); return; }
                foreach (var pt in pts) _path.Enqueue(pt);
                _path.Enqueue(target);
            });
        }

        private static float F(string s) => float.Parse(s, CultureInfo.InvariantCulture);

        private string Run(string cmd)
        {
            var p = cmd.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (p.Length == 0) return "";
            var hero = HeroMerchant.Instance;
            var gm = GameManager.Instance;
            var dm = DungeonManager.Instance;
            switch (p[0])
            {
                case "stick": _walkTarget = null; _stick = new Vector2(F(p[1]), F(p[2])); return "ok";
                case "stop": _walkTarget = null; _stick = Vector2.zero; _buttonsUntil.Clear(); return "ok";
                case "allow": hero.heroMerchantController.AllowMovement(); hero.heroMerchantController.EnableMoveInput(); return "ok";
                case "goto": PathTo(new Vector2(F(p[1]), F(p[2])), Vector2.zero); return "ok";
                case "door":
                {
                    var room = dm.currentDungeonRoom;
                    var d = room.roomDoors.FirstOrDefault(x => x != null && x.destinationRoom != null && x.name.IndexOf(p[1], StringComparison.OrdinalIgnoreCase) >= 0);
                    if (d == null) return "no door " + p[1];
                    var dir = ((Vector2)(d.transform.position - room.transform.position)).normalized;
                    PathTo((Vector2)d.transform.position - dir * 30f, dir);
                    return "ok";
                }
                case "walkto":
                    _walkTarget = new Vector2(F(p[1]), F(p[2]));
                    _walkUntil = Time.unscaledTime + (p.Length > 3 ? F(p[3]) : 6f);
                    return "ok";
                case "walk": // dx dy relative
                    _walkTarget = (Vector2)hero.transform.position + new Vector2(F(p[1]), F(p[2]));
                    _walkUntil = Time.unscaledTime + 6f;
                    return "ok";
                case "press": _buttonsUntil[p[1]] = Time.unscaledTime + (p.Length > 2 ? F(p[2]) : 0.15f); return "ok";
                case "gpress": _guiUntil[p[1]] = Time.unscaledTime + (p.Length > 2 ? F(p[2]) : 0.15f); return "ok";
                case "tp": hero.heroMerchantController.TeleportTo(new Vector3(F(p[1]), F(p[2]))); return "ok";
                case "god": if (p[1] == "on") hero.SetGodMode(); else { hero.SetNormalMode(); hero.heroMerchantStats.SetVencible(); } return "ok";
                case "inv": if (p[1] == "on") { hero.SetNormalMode(); hero.heroMerchantStats.SetInvencible(); } else hero.heroMerchantStats.SetVencible(); return "ok";
                case "dungeon": hero.LoadDungeonScene((ItemMaster.Culture)Enum.Parse(typeof(ItemMaster.Culture), p[1])); return "ok";
                case "town": hero.LoadTownScene(); return "ok";
                case "village": UnityEngine.Object.FindObjectOfType<ShopEntranceDoor>().GoToVillage(); return "ok";
                case "shop": UnityEngine.Object.FindObjectOfType<ShopEntranceDoor>().GoToShop(false); return "ok";
                case "goroom":
                {
                    int rx = int.Parse(p[1]), ry = int.Parse(p[2]);
                    var to = dm.dungeonRooms.FirstOrDefault(r => r != null && r.x == rx && r.y == ry && !r.isSecret);
                    if (to == null) return "no room";
                    dm.SpecialChangeRoomTo(to); hero.heroMerchantController.TeleportTo(to.transform.position);
                    return "ok";
                }
                case "nextfloor": hero.LoadNextLevelDungeonScene(DungeonManager.Instance.culture); return "ok";
                case "steamhost": SteamCoop.Host(); return "ok";
                case "steamstatus": return $"available={SteamCoop.Available} lobby={SteamCoop.Lobby} status={SteamCoop.Status} role={Plugin.Role}";
                case "gold": hero.addGold(int.Parse(p[1])); return "ok";
                case "hurt": hero.DealDamageToPlayer(F(p[1]), "QA"); return "ok";
                case "look": ColorMenu.Set(int.Parse(p[1]), int.Parse(p[2])); return "look=" + Looks.Mine;
                case "menu": if (ColorMenu.Instance != null) ColorMenu.Instance.SetOpen(p[1] == "on"); return "open=" + ColorMenu.Open;
                case "looks": return $"mine={Looks.Mine} " + string.Join(" ", Looks.Of.Select(kv => $"p{kv.Key}={kv.Value}"));
                case "dumpatlas":
                {
                    var will = hero.transform.Find("HeroMerchantArt/Will")?.GetComponent<SpriteRenderer>();
                    var spr = Recolor.Original(will.sprite);
                    var copy = Recolor.ReadableCopy(spr.texture);
                    var file = Plugin.StatusFile("atlas.png");
                    File.WriteAllBytes(file, ImageConversion.EncodeToPNG(copy));
                    var rect = spr.textureRect;
                    return $"{file} {spr.texture.width}x{spr.texture.height} sprite={spr.name} uv0={spr.uv[0].x * spr.texture.width:F0},{spr.uv[0].y * spr.texture.height:F0}";
                }
                case "shelves":
                {
                    var cases = UnityEngine.Object.FindObjectsOfType<Showcase>().OrderBy(s => Mathf.RoundToInt(s.transform.position.x)).ThenBy(s => Mathf.RoundToInt(s.transform.position.y)).ToList();
                    var sb = new StringBuilder(); int k = 0;
                    foreach (var c in cases) for (int i = 0; i < c.itemPositions.Count; i++, k++)
                    {
                        var st = c.itemPositions[i].itemStack;
                        sb.Append($"{k}:{(st != null && st.master != null ? st.master.name + "x" + st.Quantity + "$" + st.unitSellingPrice : "-")}@{V(c.transform.position)} ");
                    }
                    return sb.ToString();
                }
                case "shelfput": // slot bagIndex price
                {
                    var cases = UnityEngine.Object.FindObjectsOfType<Showcase>().OrderBy(s => Mathf.RoundToInt(s.transform.position.x)).ThenBy(s => Mathf.RoundToInt(s.transform.position.y)).ToList();
                    var all = cases.SelectMany(c => Enumerable.Range(0, c.itemPositions.Count).Select(i => (c, i))).ToList();
                    var (sc, si) = all[int.Parse(p[1])];
                    var inv = hero.heroMerchantInventory;
                    var item = inv.slots[int.Parse(p[2])].item;
                    if (item == null) return "empty bag slot";
                    inv.SetItem(null, int.Parse(p[2]));
                    item.unitSellingPrice = int.Parse(p[3]);
                    sc.SetItemInPosition(item, si);
                    return $"put {item.master.name}x{item.Quantity}";
                }
                case "morning": TimeManagerV2.Instance.ChangeToMorning(); return "ok";
                case "openshop": UnityEngine.Object.FindObjectOfType<ShopEntranceDoor>().OpenShop(); return "ok";
                case "closeshop": UnityEngine.Object.FindObjectOfType<ShopEntranceDoor>().CloseShop(); return "ok";
                case "charge": { var cash = ShopManager.Instance.references.shopCash; bool blocked = cash.IsInstantActionBlocked(); if (!blocked) cash.TryToSellItem(); return $"blocked={blocked}"; }
                case "shopinfo":
                {
                    var cash = ShopManager.Instance.references.shopCash;
                    var gen = ShopManager.Instance.visitorGenerator;
                    var copies = GameObject.Find("CoopCustomers");
                    return $"open={!ShopManager.Instance.IsShopClosed} visitors={gen.generatedVisitors.Count} atCash={(cash.visitorBuying != null)} guestAtCash={ShopSync.VisitorAtCash} cashItem={ShopSync.CashItem} copies={(copies != null ? copies.transform.childCount : 0)} gold={hero.GetCurrentGold()} cashPos={V(cash.transform.position)}";
                }
                case "visitorparts":
                {
                    var sb = new StringBuilder();
                    foreach (var v in ShopManager.Instance.visitorGenerator.generatedVisitors.Where(x => x != null).Take(3))
                    {
                        sb.Append($"[{v.name}] ");
                        foreach (var sr in v.GetComponentsInChildren<SpriteRenderer>(false).Where(s => s.enabled && s.sprite != null))
                            sb.Append($"{sr.name}:{sr.sprite.name}|{(sr.sharedMaterial ? sr.sharedMaterial.name + "/" + sr.sharedMaterial.shader.name : "-")}|{sr.sortingLayerName}/{sr.sortingOrder}|a{sr.color.a:F1} ");
                    }
                    return sb.ToString();
                }
                case "matprops":
                {
                    var sb = new StringBuilder();
                    var mats = new List<(string, Material)>();
                    var v0 = ShopManager.Instance.visitorGenerator.generatedVisitors.FirstOrDefault(x => x != null);
                    if (v0 != null) foreach (var sr in v0.GetComponentsInChildren<SpriteRenderer>()) if (sr.sharedMaterial) mats.Add(("visitor " + sr.name, sr.sharedMaterial));
                    var byName = SpriteCache.GetMaterial("Sprites/Diffuse Flash"); if (byName) mats.Add(("byname", byName));
                    foreach (var (label, m) in mats)
                    {
                        sb.Append($"[{label}: {m.name} shader={m.shader.name} kw={string.Join(",", m.shaderKeywords)}");
                        foreach (var prop in new[] { "_FlashAmount", "_FlashColor", "_Color", "_SelfIllum", "_Flash", "_FlashColour", "_Amount", "_OutlineColor", "_Outline", "_Silhouette", "_TintColor", "_RendererColor" })
                            if (m.HasProperty(prop)) { try { sb.Append($" {prop}={m.GetColor(prop)}"); } catch { } try { sb.Append($"/{m.GetFloat(prop)}"); } catch { } }
                        sb.Append("] ");
                    }
                    return sb.ToString();
                }
                case "info": return Info();
                default: return "unknown " + p[0];
            }
        }

        private static string V(Vector3 v) => $"{v.x:F0},{v.y:F0}";

        private static string Info()
        {
            var hero = HeroMerchant.Instance; var dm = DungeonManager.Instance; var gm = GameManager.Instance;
            var sb = new StringBuilder();
            var hc = hero != null ? hero.heroMerchantController : null;
            if (hc != null) sb.Append($"stick={hc.inputDevice.LeftStick.X:F2},{hc.inputDevice.LeftStick.Y:F2} canMove={hc.canMove} panel={(GUIManager.Instance.currentInventory ? GUIManager.Instance.currentInventory.name : "none")} notLoading={GameManager.Instance.NotLoadingScene} moveInput={hc.IsMoveInputEnabled()} vel={hc._velX:F1},{hc._velY:F1} enabledSet={hc.inputDevice.Enabled} tags={string.Join("/", new[] { "IdleShop", "WalkShop", "Idle", "Walk", "WalkGrass" }.Where(tg => hc.characterAnimator.GetCurrentAnimatorStateInfo(0).IsTag(tg)))} trans={hc.characterAnimator.IsInTransition(0)} paused={GameManager.Instance.isGamePaused} ts={Time.timeScale} ");
            sb.Append($"injected={Driver.Injected} zone={Session.LocalZone()} pos={(hero ? V(hero.transform.position) : "-")} hp={(hero ? (float)hero.heroMerchantStats.currentHealth.Value : 0):F0}");
            if (hero) sb.Append($" gold={hero.GetCurrentGold()} bag=[{string.Join(",", hero.heroMerchantInventory.slots.Where(s => s.item != null && s.item.master != null).Select(s => s.item.master.name + "x" + s.item.Quantity))}]");
            sb.Append($" ghost={Downed.Me} partnerGhost={Downed.Partner} partner={(Puppet.Instance ? V(Puppet.Instance.transform.position) + (Puppet.Instance.Visible ? "" : "(away)") : "-")}");
            if (dm != null && dm.currentDungeonRoom != null && gm.IsDungeonSceneLoaded())
            {
                var r = dm.currentDungeonRoom;
                sb.Append($" room={V(r.transform.position)}");
                var heal = dm.healingDungeonRoom;
                if (heal != null) sb.Append($" fountainRoom={heal.x},{heal.y}");
                var area = UnityEngine.Object.FindObjectsOfType<HealingArea>().FirstOrDefault(h => h.transform.IsChildOf(r.transform));
                if (area != null) sb.Append($" fountain={V(area.transform.position)}");
                sb.Append(" enemies=[" + string.Join(";", r.roomEnemies.Where(e => e != null && e.enemyStats != null && e.enemyStats.isAlive)
                    .Select(e => $"{e.name.Replace("(Clone)", "")}@{V(e.transform.position)}#{EnemySync.IdOf(e)}")) + "]");
                sb.Append(" doors=[" + string.Join(";", r.roomDoors.Where(d => d != null && d.destinationRoom != null)
                    .Select(d => $"{d.name}@{V(d.transform.position)}->{d.destinationRoom.x},{d.destinationRoom.y}{(d.isOpened ? "" : "(closed)")}")) + "]");
                sb.Append(" items=[" + string.Join(";", UnityEngine.Object.FindObjectsOfType<ItemStack>().Where(s => s.gameObject.activeInHierarchy && s.pickOnWalk != null && !s.transform.IsChildOf(hero.transform))
                    .Select(s => $"{s.master?.name}@{V(s.transform.position)}")) + "]");
            }
            return sb.ToString();
        }
    }

    // Feed the virtual pad into Will's actions after they read their real bindings this frame, then commit again.
    // (Commit itself is small enough to be inlined, so patch the action's Update instead.)
    [HarmonyPatch(typeof(PlayerAction), "Update")]
    static class VirtualPadInput
    {
        static void Postfix(PlayerAction __instance, ulong updateTick, float deltaTime)
        {
            if (Driver.Pad.Count == 0 || !Driver.Pad.TryGetValue(__instance, out float v)) return;
            __instance.UpdateWithValue(v, updateTick, deltaTime);
            __instance.Commit();
            Driver.Injected++;
        }
    }
}











