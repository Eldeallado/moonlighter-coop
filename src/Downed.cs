using System.IO;
using HarmonyLib;
using UnityEngine;

namespace MoonlighterCoop
{
    /// In a dungeon, dying with your partner still up leaves you downed: you stay where you fell and watch.
    /// Your partner revives you by standing next to you for a moment, or by healing in a fountain.
    /// If both go down, the run ends for both as usual.
    public static class Downed
    {
        public static bool Me;          // the local Will is down
        /// Other players who are ghosts right now (by player number).
        public static readonly System.Collections.Generic.HashSet<int> Ghosts = new System.Collections.Generic.HashSet<int>();
        public static bool Partner => Ghosts.Count > 0;
        public static bool EveryoneElseDown => Ghosts.Count >= Session.Players - 1;
        public static bool IsGhost(int player) => Ghosts.Contains(player);
        public static bool EndingRun;   // let the game's own death run (both down)
        private static float _reviveProgress;
        private static float _nextFountainMsg, _nextFountainCheck;
        public static int Revives;

        public const float ReviveRange = 32f;
        public const float ReviveTime = 2.5f;

        public static bool InDungeon => Net.Juego && GameManager.Instance != null && GameManager.Instance.IsDungeonSceneLoaded();

        /// The local Will's death animation ended. True = let the game end the run.
        public static bool OnLocalDeathEnded()
        {
            if (EndingRun || !InDungeon) return true;
            if (EveryoneElseDown || HostSettings.OneHeart)   // "one heart" (host setting): anyone falls, the run ends
            {
                // Everyone down: end the run on both PCs.
                Plugin.Log.LogInfo("[downed] both players are down: the run ends");
                Net.Send(Msg.EndRun, new byte[0]);
                EndRun();
                return false;
            }
            Me = true;
            Stats.Count(Stats.Deaths);
            BecomeGhost();
            Net.Send(Msg.Downed, new byte[] { 1, (byte)Session.MyId });
            Plugin.Log.LogInfo("[downed] I am a ghost; my partner can revive me");
            return false;
        }

        private static string _ghostFloor = "";

        /// Joined in the middle of a floor (host setting): watch as a ghost until the next floor.
        public static bool SpectatePending;
        public static void Spectate()
        {
            var hero = HeroMerchant.Instance;
            if (hero == null || Me || !InDungeon) return;
            Me = true;
            BecomeGhost();
            Net.Send(Msg.Downed, new byte[] { 1, (byte)Session.MyId });
            Banner.Show("Te has unido a mitad de piso: miras como fantasma hasta el siguiente piso");
            Plugin.Log.LogInfo("[downed] joined mid-floor: spectating as a ghost");
        }

        /// Isaac-style: the dead player floats around as a see-through ghost. It flies over holes, cannot attack,
        /// pick things up or be hurt, and enemies ignore it.
        private static void BecomeGhost()
        {
            var hero = HeroMerchant.Instance;
            var c = hero.heroMerchantController;
            var st = hero.heroMerchantStats;
            try { c.SetPlayerShaderToDefault(); } catch { }   // the game never clears the red damage flash here
            // The death sequence stops time (it would show the results next): start it again.
            GameManager.Instance.UnpauseGame();
            GameManager.Instance.SetDefaultGameSpeed();
            c.SetDefaultMode();
            c.ResetHeroMerchantAnimator();
            c.EnableHeroMerchantArt();
            c.ShowHeroMerchantArmor();
            st.isDead = false;
            st.SetInvencible();
            hero.SetFlying(true);
            c.ForbidAttack();
            c.DisableAttackInput();
            c.EnableMoveInput();
            c.AllowMovement();
            var dm = DungeonManager.Instance;
            _ghostFloor = dm != null ? $"{dm.culture}:{dm.level}" : "";
        }

        public static void EndRun()
        {
            var hero = HeroMerchant.Instance;
            if (hero == null) return;
            EndingRun = true;
            Vote.Reset();
            Dungeon.ApplyingRemote = true;
            try { hero.OnWillDeathAnimationEnded(); }
            finally { Dungeon.ApplyingRemote = false; }
        }

        public static void OnEndRun()
        {
            if (!Me && !(HeroMerchant.Instance != null && (bool)HeroMerchant.Instance.heroMerchantStats.isDead)) return;
            EndRun();
        }

        public static void OnPartnerDowned(byte[] data)
        {
            int who = data.Length > 1 ? data[1] : 0;
            if (data.Length > 0 && data[0] == 1) Ghosts.Add(who); else Ghosts.Remove(who);
            _reviveProgress = 0f;
        }

        public static void ReviveMe(float healthFraction)
        {
            var hero = HeroMerchant.Instance;
            if (hero == null || !Me) return;
            Me = false;
            var c = hero.heroMerchantController;
            var st = hero.heroMerchantStats;
            SetGhost(hero, false);
            try { c.SetPlayerShaderToDefault(); } catch { }
            hero.SetFlying(false);
            c.AllowAttack();
            c.EnableAttackInput();
            c.SetDefaultMode();
            c.ResetHeroMerchantAnimator();
            c.EnableHeroMerchantArt();
            c.ShowHeroMerchantArmor();
            st.RecoverAllHealth();
            bool v = st.currentHealth.variableStat;
            st.currentHealth.variableStat = true;
            st.currentHealth.Value = Mathf.Max(1f, st.currentHealth.maxValue * healthFraction);
            st.currentHealth.variableStat = v;
            st.SetInvencible(2f);
            c.EnableMoveInput();
            c.AllowMovement();
            Revives++;
            Net.Send(Msg.Downed, new byte[] { 0, (byte)Session.MyId });
            Plugin.Log.LogInfo("[downed] revived");
        }

        private static void ClearGhost(HeroMerchant hero)
        {
            Me = false;
            var c = hero.heroMerchantController;
            SetGhost(hero, false);
            hero.SetFlying(false);
            c.AllowAttack();
            c.EnableAttackInput();
            hero.heroMerchantStats.SetVencible();
            hero.heroMerchantStats.RecoverAllHealth();
            Net.Send(Msg.Downed, new byte[] { 0, (byte)Session.MyId });
            Plugin.Log.LogInfo("[downed] back in town: no longer a ghost");
        }

        private static void SetGhost(HeroMerchant hero, bool ghost)
        {
            var art = hero.transform.Find("HeroMerchantArt");
            if (art == null) return;
            foreach (var sr in art.GetComponentsInChildren<SpriteRenderer>(true))
            {
                if (sr.name.Contains("Light")) continue;
                sr.color = ghost ? new Color(0.75f, 0.85f, 1f, 0.5f) : Color.white;
            }
        }

        public static void OnRevive(byte[] data)
        {
            var r = new BinaryReader(new MemoryStream(data));
            float frac = r.ReadSingle();
            if (data.Length >= 5 && r.ReadByte() != Session.MyId) return;   // a revive for someone else
            bool wasMe = Me;
            ReviveMe(frac);
            if (data.Length >= 5 && wasMe) Banner.Show("Un compañero te ha revivido con una poción");
        }

        /// The living can bring a ghost back on the spot by spending the potion they carry (R, or L3 on a pad),
        /// standing next to it: a third of its health, and the potion is gone.
        public static void TryPotionRevive()
        {
            var hero = HeroMerchant.Instance;
            if (!InDungeon || Me || Ghosts.Count == 0 || hero == null) return;
            bool healer = Roles.Is(Roles.Healer);   // from further away, and twice the health
            Puppet near = null; float best = healer ? 110f : 56f;
            foreach (var p in Puppet.All.Values)
            {
                if (p == null || !p.Visible || !IsGhost(p.PlayerId)) continue;
                float d = ((Vector2)(p.transform.position - hero.transform.position)).magnitude;
                if (d < best) { best = d; near = p; }
            }
            if (near == null) { Banner.Show("Acércate a tu compañero caído para revivirlo"); return; }
            var inv = hero.heroMerchantInventory;
            var potion = inv.GetEquippedItemByType(HeroMerchantInventory.EquipmentSlot.Potion);
            if (potion == null || potion.Quantity <= 0) { Banner.Show("Necesitas una poción equipada para revivirlo"); return; }
            potion.Quantity -= 1;
            if (potion.Quantity > 0) HUDManager.Instance.SetPotionQuantity(potion.Quantity);
            else { HUDManager.Instance.SetEmptyConsumableIcon(); inv.SetEquippedItemByType(null, HeroMerchantInventory.EquipmentSlot.Potion); }
            var ms = new MemoryStream(); var w = new BinaryWriter(ms); w.Write((healer ? 0.7f : 0.35f) * (HostSettings.HardRevive ? 0.6f : 1f)); w.Write((byte)near.PlayerId);
            Net.Send(Msg.Revive, ms.ToArray());
            Ghosts.Remove(near.PlayerId);
            PotionRevives++;
            Stats.Count(Stats.Revive);
            Banner.Show("Has gastado una poción para revivir a tu compañero");
            Plugin.Log.LogInfo($"[downed] revived player {near.PlayerId} with a potion");
        }
        public static int PotionRevives;
        private static float _spectateAt;
        public static void OnSpectate() { if (HostSettings.Spectator) { SpectatePending = true; _spectateAt = Time.unscaledTime + 3f; } }

        public static void Tick()
        {
            var hero = HeroMerchant.Instance;
            if (SpectatePending && InDungeon && hero != null && GameManager.Instance.NotLoadingScene && Time.unscaledTime > _spectateAt) { SpectatePending = false; Spectate(); }
            if (!InDungeon || hero == null)
            {
                if (!Net.Juego || GameManager.Instance == null || !GameManager.Instance.IsDungeonSceneLoaded())
                {
                    // Out of the dungeon (run over, or gone home): nobody stays a ghost.
                    if (Me && hero != null && GameManager.Instance != null && GameManager.Instance.NotLoadingScene) ClearGhost(hero);
                    if (!Me) { Ghosts.Clear(); EndingRun = false; }
                }
                return;
            }
            if (Me)
            {
                hero.heroMerchantController.DisableAttackInput();
                hero.heroMerchantController.ForbidAttack();
                SetGhost(hero, true);   // see-through while a ghost (the partner's copy shows it too)
                // A new floor brings ghosts back, like Isaac.
                var dmg = DungeonManager.Instance;
                // (Hard revive rule: not any more — only a fountain or a potion.)
                if (dmg != null && GameManager.Instance.NotLoadingScene && $"{dmg.culture}:{dmg.level}" != _ghostFloor && (!HostSettings.HardRevive || HostSettings.Race)) ReviveMe(0.5f);
                return;
            }
            // No revive by touch: the living player must reach a fountain, or beat the floor.
            // The fountain only heals someone hurt, and checked once on entering, so a living player at full health
            // walking in never counted. Watch it instead: while I am in the water and someone is a ghost, revive them.
            if (Partner && (Input.GetKeyDown(KeyCode.R) || PadNav.L3Down)) TryPotionRevive();
            if (Partner && Time.unscaledTime >= _nextFountainCheck)
            {
                _nextFountainCheck = Time.unscaledTime + 0.3f;
                foreach (var area in Object.FindObjectsOfType<HealingArea>())
                {
                    var tr = Traverse.Create(area);
                    if (!tr.Field("isHeroMerchantIn").GetValue<bool>()) continue;
                    var ac = tr.Field("areaCollider").GetValue<Collider2D>();
                    var hc = tr.Field("heroMerchantCollider").GetValue<Collider2D>();
                    if (ac != null && hc != null && !ac.IsTouching(hc)) continue;
                    Plugin.Log.LogInfo("[downed] I am in a fountain: ghosts come back");
                    Stats.Count(Stats.Revive, Ghosts.Count);
                    LocalFountainHealed();
                    break;
                }
            }
        }

        public static string Hint()
        {
            if (Me) return HostSettings.HardRevive ? "Eres un fantasma: solo revives si tu compañero entra en una fuente o gasta una poción a tu lado" : "Eres un fantasma: revives si tu compañero entra en una fuente o pasáis al siguiente piso";
            if (Partner) return "Tu compañero ha caído: llega a una fuente, pasa al siguiente piso o ponte a su lado y pulsa R (L3) para gastar una poción";
            return null;
        }

        // Fountain: healing in it heals (and revives) the partner too.
        public static void LocalFountainHealed()
        {
            if (!InDungeon || Time.unscaledTime < _nextFountainMsg) return;
            _nextFountainMsg = Time.unscaledTime + 0.5f;
            Net.Send(Msg.FountainHeal, new byte[0]);
        }

        public static void OnFountainHeal()
        {
            var hero = HeroMerchant.Instance;
            if (hero == null) return;
            // The partner used a fountain: a ghost comes back. (The one in the fountain heals as usual.)
            if (Me) ReviveMe(HostSettings.HardRevive ? 0.25f : 0.5f);
        }
    }

    [HarmonyPatch(typeof(HeroMerchant), nameof(HeroMerchant.OnWillDeathAnimationEnded))]
    static class DownedInsteadOfDeath
    {
        public static int Calls;
        static bool Prefix()
        {
            Calls++;
            if (MoonlighterEffectRegistry.GetEffect<AngelAmuletEffect>() != null) return true; // the amulet revives as usual
            return Downed.OnLocalDeathEnded();
        }
    }

    [HarmonyPatch(typeof(HeroMerchant), nameof(HeroMerchant.DealDamageToPlayer))]
    static class GhostsTakeNoDamage
    {
        static bool Prefix() => !Downed.Me;
    }

    [HarmonyPatch(typeof(PickOnWalk), nameof(PickOnWalk.TryToAdd))]
    static class GhostsPickNothing
    {
        [HarmonyPriority(Priority.First)]
        static bool Prefix() => !Downed.Me;
    }

    [HarmonyPatch(typeof(HealingArea), "ApplyHealingEffect")]
    static class FountainHealsBoth
    {
        static bool Prefix(out float __state)
        {
            __state = HeroMerchant.Instance != null ? (float)HeroMerchant.Instance.heroMerchantStats.currentHealth.Value : 0f;
            return !Downed.Me;   // a ghost cannot use the fountain itself
        }
    }

    // A ghost splashing into a fountain must not lose its protection or start healing.
    [HarmonyPatch(typeof(HealingArea), "OnTriggerEnter2D")]
    static class GhostsIgnoreFountains
    {
        static bool Prefix() => !Downed.Me;
    }
}

