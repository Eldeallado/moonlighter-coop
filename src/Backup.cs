using System;
using System.IO;
using System.Linq;
using Steamworks;
using UnityEngine;

namespace MoonlighterCoop
{
    /// Before playing online (creating or joining a game), a copy of this player's own saves: the Steam Cloud
    /// files of Moonlighter and anything in the game's data folder, into BepInEx\copias-partidas\<date>.
    /// The last 5 copies are kept. Restoring is by hand (the folder says how).
    public static class Backup
    {
        private static float _last = -1000f;
        public static string Root => Path.Combine(BepInEx.Paths.BepInExRootPath, "copias-partidas");
        public static int Count => Directory.Exists(Root) ? Directory.GetDirectories(Root).Length : 0;

        public static void Now(string why)
        {
            if (Plugin.Drive || Plugin.Bot) return;                          // test copies share the player's cloud: never
            if (Time.unscaledTime - _last < 600f) return;                     // at most every 10 minutes
            _last = Time.unscaledTime;
            try
            {
                string dir = Path.Combine(Root, DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss"));
                Directory.CreateDirectory(dir);
                int n = 0;
                if (SteamCoop.Available)
                {
                    int count = SteamRemoteStorage.GetFileCount();
                    for (int i = 0; i < count; i++)
                    {
                        string name = SteamRemoteStorage.GetFileNameAndSize(i, out int size);
                        if (size <= 0 || string.IsNullOrEmpty(name)) continue;
                        var buf = new byte[size];
                        if (SteamRemoteStorage.FileRead(name, buf, size) != size) continue;
                        string safe = string.Join("_", name.Split(Path.GetInvalidFileNameChars()));
                        File.WriteAllBytes(Path.Combine(dir, "steam_" + safe), buf); n++;
                    }
                }
                string local = Application.persistentDataPath;
                if (Directory.Exists(local))
                    foreach (var f in Directory.GetFiles(local))
                    {
                        if (new FileInfo(f).Length > 20 * 1024 * 1024) continue;
                        File.Copy(f, Path.Combine(dir, "local_" + Path.GetFileName(f)), true); n++;
                    }
                File.WriteAllText(Path.Combine(dir, "LEEME.txt"),
                    "Copia de tus partidas de Moonlighter hecha por Moonlighter Coop antes de jugar online (" + why + ").\r\n" +
                    "Para recuperarla: los archivos steam_* son los de la nube de Steam; los local_* van en " + local + ".\r\n" +
                    "Si algo se estropeó, pide ayuda antes de tocar nada.\r\n");
                // Keep the last 5.
                foreach (var old in Directory.GetDirectories(Root).OrderByDescending(d => d).Skip(5)) Directory.Delete(old, true);
                Plugin.Log.LogInfo($"[backup] {n} files -> {dir}");
                if (n > 0) Banner.Show("Copia de seguridad de tus partidas hecha");
            }
            catch (Exception e) { Plugin.Log.LogWarning("[backup] " + e.Message); }
        }
    }
}
