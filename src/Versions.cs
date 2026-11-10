using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

namespace MoonlighterCoop
{
    /// Two things about versions:
    /// - Players with a different network protocol cannot play together: the host turns them away with a clear
    ///   message instead of letting the game break silently. The hello carries "version|pPROTOCOL".
    /// - Once per launch, ask GitHub for the latest release and, if it is newer, say so (F2 menu + a scroll).
    public static class Versions
    {
        /// Bump whenever messages change in a way older versions would misread.
        public const int Protocol = 6;
        public const string Repo = "Eldeallado/moonlighter-coop";
        public const string ReleasesUrl = "https://github.com/" + Repo + "/releases/latest";

        public static string Hello => Plugin.Version + "|p" + Protocol;

        public static void Parse(string hello, out string version, out int protocol)
        {
            version = hello; protocol = 0;   // versions before 0.4.0 sent just "0.x.y"
            int bar = hello.IndexOf("|p");
            if (bar < 0) return;
            version = hello.Substring(0, bar);
            int.TryParse(hello.Substring(bar + 2), out protocol);
        }

        // ---------- update check ----------

        public static string Latest = "";
        public static bool UpdateAvailable;
        private static bool _started;

        public static void StartCheck(MonoBehaviour host)
        {
            if (_started) return;
            _started = true;
            host.StartCoroutine(Check());
        }

        private static IEnumerator Check()
        {
            yield return new WaitForSecondsRealtime(5f);
            using (var req = UnityWebRequest.Get("https://api.github.com/repos/" + Repo + "/releases/latest"))
            {
                req.SetRequestHeader("User-Agent", "MoonlighterCoop/" + Plugin.Version);
                req.SetRequestHeader("Accept", "application/vnd.github+json");
                req.timeout = 10;
                yield return req.SendWebRequest();
                if (req.isNetworkError || req.isHttpError) { Plugin.Log.LogInfo("[update] could not check: " + req.error); yield break; }
                // Only the tag is needed: "tag_name":"v0.4.0"
                var m = System.Text.RegularExpressions.Regex.Match(req.downloadHandler.text, "\"tag_name\"\\s*:\\s*\"v?([0-9][0-9.]*)\"");
                if (!m.Success) yield break;
                Latest = m.Groups[1].Value;
                UpdateAvailable = Newer(Latest, Plugin.Version);
                Plugin.Log.LogInfo($"[update] latest {Latest}, mine {Plugin.Version}, update={UpdateAvailable}");
                if (UpdateAvailable) Banner.Show($"Hay una versión nueva del mod ({Latest}). " + Teclas.Nombre("Pulsa F2 para descargarla.", "Actualiza el juego del móvil para tenerla."));
            }
        }

        public static bool Newer(string a, string b)
        {
            var pa = a.Split('.'); var pb = b.Split('.');
            for (int i = 0; i < 3; i++)
            {
                int x = i < pa.Length && int.TryParse(pa[i], out var xa) ? xa : 0;
                int y = i < pb.Length && int.TryParse(pb[i], out var yb) ? yb : 0;
                if (x != y) return x > y;
            }
            return false;
        }
    }
}
