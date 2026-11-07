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
        public static bool Partner;     // the other Will is down
        public static bool EndingRun;   // let the game's own death run (both down)
        private static float _reviveProgress;
        private static float _nextFountainMsg;
        public static int Revives;

        public const float ReviveRange = 32f;
        public const float ReviveTime = 2.5f;

        public static bool InDungeon => Net.Connected && GameManager.Instance != null && GameManager.Instance.IsDungeonSceneLoaded();

        /// The local Will's death animation ended. True = let the game end the run.
        public static bool OnLocalDeathEnded()
        {
            if (EndingRun || !InDungeon) return true;
            if (Partner)
            {
                // Both down: end the run on both PCs.
                Plugin.Log.LogInfo("[downed] both players are down: the run ends");
                Net.Send(Msg.EndRun, new byte[0]);
                EndRun();
                return false;
            }
            Me = true;
            BecomeGhost();
            Net.Send(Msg.Downed, new byte[] { 1 });
            Plugin.Log.LogInfo("[downed] I am a ghost; my partner can revive me");
            return false;
        }

        private static string _ghostFloor = "";

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

        public static void OnPartnerDowned(byte[] data) { Partner = data.Length > 0 && data[0] == 1; _reviveProgress = 0f; }

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
            Net.Send(Msg.Downed, new byte[] { 0 });
            Plugin.Log.LogInfo("[downed] revived");
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
            float frac = new BinaryReader(new MemoryStream(data)).ReadSingle();
            ReviveMe(frac);
        }

        public static void Tick()
        {
            var hero = HeroMerchant.Instance;
            if (!InDungeon || hero == null) { if (!Net.Connected || GameManager.Instance == null || !GameManager.Instance.IsDungeonSceneLoaded()) { Me = false; Partner = false; EndingRun = false; } return; }
            if (Me)
            {
                hero.heroMerchantController.DisableAttackInput();
                hero.heroMerchantController.ForbidAttack();
                SetGhost(hero, true);   // see-through while a ghost (the partner's copy shows it too)
                // A new floor brings ghosts back, like Isaac.
                var dmg = DungeonManager.Instance;
                if (dmg != null && GameManager.Instance.NotLoadingScene && $"{dmg.culture}:{dmg.level}" != _ghostFloor) ReviveMe(0.5f);
                return;
            }
            // No revive by touch: the living player must reach a fountain, or beat the floor.
        }

        public static string Hint()
        {
            if (Me) return "Eres un fantasma: revives si tu compañero entra en una fuente o pasáis al siguiente piso";
            if (Partner) return "Tu compañero ha caído: llega a una fuente o pasa al siguiente piso para revivirlo";
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
            if (Me) ReviveMe(0.5f);
        }
    }

    [HarmonyPatch(typeof(HeroMerchant), nameof(HeroMerchant.OnWillDeathAnimationEnded))]
    static class DownedInsteadOfDeath
    {
        static bool Prefix()
        {
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
        static void Postfix(HealingArea __instance, float __state)
        {
            var h = HeroMerchant.Instance;
            if (h == null || Downed.Me || !Downed.Partner) return;
            // Only if the living player is really in the water (a teleport out can leave the area thinking it is).
            var col = __instance.GetComponent<Collider2D>();
            if (col != null && !col.OverlapPoint(h.transform.position)) return;
            Downed.LocalFountainHealed();
        }
    }

    // A ghost splashing into a fountain must not lose its protection or start healing.
    [HarmonyPatch(typeof(HealingArea), "OnTriggerEnter2D")]
    static class GhostsIgnoreFountains
    {
        static bool Prefix() => !Downed.Me;
    }
}

