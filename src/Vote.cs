using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace MoonlighterCoop
{
    /// End of a run (everyone down): each player votes on the results screen, "play again" (A) or "go to the
    /// village" (B). The majority wins; a tie goes to the host's vote. Built for N players.
    public static class Vote
    {
        public enum Choice : byte { None = 0, Replay = 1, Village = 2 }

        public static Choice Mine;
        public static readonly Dictionary<string, Choice> Others = new Dictionary<string, Choice>();
        public static bool Deciding;            // running the decided button on our own panel
        public static bool SuppressTravel;      // guest: do the panel's clean-up but let the host lead the trip
        public static int Decisions;

        public static int Players => Session.Players;

        public static void Reset() { Mine = Choice.None; Others.Clear(); }

        public static void Cast(Choice c)
        {
            Mine = c;
            Net.Send(Msg.Vote, new[] { (byte)c });
            Plugin.Log.LogInfo($"[vote] I vote {c}");
            if (Session.IsHost) TryDecide();
        }

        public static void OnVote(int from, byte[] data)
        {
            Others["p" + from] = (Choice)data[0];
            Plugin.Log.LogInfo($"[vote] player {from} votes {(Choice)data[0]}");
            if (Session.IsHost) TryDecide();
        }

        private static void TryDecide()
        {
            var all = new List<Choice>();
            if (Mine != Choice.None) all.Add(Mine);
            all.AddRange(Others.Values.Where(v => v != Choice.None));
            if (all.Count < Players) return;
            int replay = all.Count(v => v == Choice.Replay), village = all.Count(v => v == Choice.Village);
            var result = replay > village ? Choice.Replay : village > replay ? Choice.Village : Mine;   // tie: host
            Plugin.Log.LogInfo($"[vote] replay {replay} / village {village} -> {result}");
            Net.Send(Msg.VoteResult, new[] { (byte)result });
            Apply(result);
        }

        public static void OnResult(byte[] data) => Apply((Choice)data[0]);

        /// Press the winning button on our own results screen.
        private static void Apply(Choice c)
        {
            var panel = Object.FindObjectOfType<DungeonResultsPanel>();
            Reset();
            Decisions++;
            if (panel == null) return;
            Deciding = true;
            SuppressTravel = Session.IsGuest;   // the guest follows the host's Travel instead of travelling itself
            try
            {
                Traverse.Create(panel).Field("_ReplayPressed").SetValue(c == Choice.Replay);
                // The panel only replays after a normal death; in co-op the run ended through the ghosts.
                if (c == Choice.Replay) Traverse.Create(panel).Field("_willDied").SetValue(true);
                Traverse.Create(panel).Method("EndResult").GetValue();
            }
            finally { Deciding = false; SuppressTravel = false; }
        }

        public static string Hint()
        {
            if (!Net.Juego || !(GUIManager.Instance != null && GUIManager.Instance.currentInventory is DungeonResultsPanel)) return null;
            // En el móvil, dos botones grandes con el texto de cada voto (el panel del juego solo dice A y B)
            if (Teclas.Movil) { Teclas.OfrecerBoton("A", "Jugar de nuevo"); Teclas.OfrecerBoton("B", "Ir a la villa"); }
            string Name(Choice c) => c == Choice.Replay ? "jugar de nuevo" : c == Choice.Village ? "ir a la villa" : "…";
            int votes = (Mine != Choice.None ? 1 : 0) + Others.Count;
            return $"Votación — tú: {Name(Mine)}   ({votes}/{Players} han votado; gana la mayoría, empate: anfitrión)";
        }
    }

    [HarmonyPatch(typeof(DungeonResultsPanel), nameof(DungeonResultsPanel.OnAButtonReleased))]
    static class VoteReplay
    {
        static bool Prefix(ref bool __result)
        {
            // Only a run that ended with everyone down is voted; going home with the pendant just goes home.
            if (!Net.Juego || Vote.Deciding || !Downed.EndingRun) return true;
            Vote.Cast(Vote.Choice.Replay);
            __result = true;
            return false;
        }
    }

    [HarmonyPatch(typeof(DungeonResultsPanel), nameof(DungeonResultsPanel.OnBButtonPressed))]
    static class VoteVillage
    {
        static bool Prefix(ref bool __result)
        {
            // Only a run that ended with everyone down is voted; going home with the pendant just goes home.
            if (!Net.Juego || Vote.Deciding || !Downed.EndingRun) return true;
            Vote.Cast(Vote.Choice.Village);
            __result = true;
            return false;
        }
    }

    // Host: the results screen travels through GameManager directly; tell the guest.
    [HarmonyPatch(typeof(GameManager), nameof(GameManager.LoadTownSceneFromDungeon))]
    static class HostTownFromResults
    {
        static bool Prefix() => !(Vote.SuppressTravel && Session.IsGuest);
        static void Postfix() { if (Session.IsHost && !Dungeon.InHeroTravel) Dungeon.SendTravel(Dungeon.Travel.Town, ItemMaster.Culture.Merchant, 0); }
    }

    [HarmonyPatch(typeof(GameManager), nameof(GameManager.LoadDungeonScene))]
    static class HostReplayFromResults
    {
        static bool Prefix(ItemMaster.Culture culture, int level, bool replay)
        {
            if (Vote.SuppressTravel && Session.IsGuest) return false;
            if (Session.IsHost && Net.Juego && !Dungeon.InHeroTravel && level == 0)
            {
                Dungeon.FloorSeed = new System.Random().Next(1, int.MaxValue);
                Dungeon.SendTravel(Dungeon.Travel.NextLevel, culture, 0);
            }
            return true;
        }
    }
}
