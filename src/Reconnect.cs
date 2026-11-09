using System.Collections.Generic;
using System.IO;
using System.Linq;
using Steamworks;
using UnityEngine;

namespace MoonlighterCoop
{
    /// Losing a player mid-run does not end it.
    /// - Guest side: if the connection drops (not a goodbye), keep trying for 90 s: same Steam lobby, or the same
    ///   IP. On the way back the guest goes to the main menu and loads the host's save again, like a new join.
    /// - Host side: whenever a guest finishes loading in town while the host is in a dungeon (a reconnect, or a
    ///   friend joining late), send them straight to the host's floor and room, with the enemies already killed
    ///   removed, plus the current shared bag and gold.
    public static class Reconnect
    {
        // ---------- guest ----------
        private static bool _wasConnected;
        public static bool Trying;
        private static float _until, _nextTry;
        private static CSteamID _lobby = CSteamID.Nil;

        public static void GuestTick(float now)
        {
            if (!Session.IsGuest) { _wasConnected = false; Trying = false; return; }
            bool connected = Net.Connected;
            if (connected)
            {
                if (Trying && Session.Welcomed) { Trying = false; SteamCoop.Status = "¡Reconectado! Volviendo a la partida…"; Plugin.Log.LogInfo("[reconnect] back in"); }
                _wasConnected = true;
                if (SteamCoop.Lobby != CSteamID.Nil) _lobby = SteamCoop.Lobby;
                return;
            }
            if (_wasConnected && !Trying)
            {
                // Dropped without a goodbye: start over as a fresh join, but keep where we were going.
                Trying = true; _wasConnected = false;
                _until = now + 90f; _nextTry = now + 3f;
                Plugin.Log.LogWarning("[reconnect] lost the host: trying to get back");
                SteamCoop.Status = "Se ha perdido la conexión con el anfitrión: reconectando…";
                Net.CloseAll();
                Session.Reset();
                if (SteamCoop.Lobby != CSteamID.Nil && SteamCoop.Available) SteamMatchmaking.LeaveLobby(SteamCoop.Lobby);
                SteamCoop.Lobby = CSteamID.Nil;
                if (HeroMerchant.Instance != null && GameManager.Instance != null) GameManager.Instance.ReturnToMainMenu();
                return;
            }
            if (!Trying) return;
            if (now > _until)
            {
                Trying = false;
                SteamCoop.Status = "No se ha podido volver a conectar con el anfitrión";
                CoopMenu.HostClosed();
                return;
            }
            // TCP retries by itself (Net.TickJoin); Steam needs to re-enter the lobby.
            if (!Plugin.TcpMode && _lobby != CSteamID.Nil && now >= _nextTry)
            {
                _nextTry = now + 10f;
                Plugin.Log.LogInfo("[reconnect] re-entering the lobby");
                SteamCoop.Join(_lobby);
            }
        }

        // ---------- host ----------
        private static readonly HashSet<IPeer> _pending = new HashSet<IPeer>();
        private static readonly Dictionary<IPeer, float> _seenInTown = new Dictionary<IPeer, float>();
        public static int CatchUps;

        /// Called when the host sends its save to a guest.
        public static void Joined(IPeer p) { _pending.Add(p); }

        public static void HostTick()
        {
            if (!Session.IsHost || _pending.Count == 0) return;
            foreach (var p in _pending.ToList())
            {
                if (p.Closed) { _pending.Remove(p); continue; }
                // Wait until the guest is in the game (its Will shows up in town).
                if (!Puppet.All.TryGetValue(p.Id, out var pup) || pup == null || !pup.Zone.StartsWith("town")) { _seenInTown.Remove(p); continue; }
                // Give the guest's town a few seconds to finish loading before sending it anywhere.
                if (!_seenInTown.TryGetValue(p, out var since)) { _seenInTown[p] = Time.unscaledTime; continue; }
                if (Time.unscaledTime - since < 6f) continue;
                _seenInTown.Remove(p);
                _pending.Remove(p);
                Shared.SendStateTo(p);
                if (GameManager.Instance != null && GameManager.Instance.IsDungeonSceneLoaded() && !Session.TutorialActivo) Dungeon.CatchUp(p);
            }
        }

        public static void Reset() { _pending.Clear(); _seenInTown.Clear(); }
    }
}
