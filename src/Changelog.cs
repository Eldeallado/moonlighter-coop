using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace MoonlighterCoop
{
    /// What each version brought: CHANGELOG.md is built into the DLL and shown in F2 → Novedades.
    /// It opens by itself the first time the game runs a version this player has not seen yet.
    public static class Changelog
    {
        public class Entry { public string Title; public List<string> Lines = new List<string>(); }

        private static List<Entry> _entries;
        public static List<Entry> Entries { get { if (_entries == null) _entries = Load(); return _entries; } }

        /// The newest version this player has looked at (config Cooperativo/NovedadesVistas).
        public static string Seen = "";
        public static bool Unseen => Seen != Plugin.Version;

        private static List<Entry> Load()
        {
            var list = new List<Entry>();
            try
            {
                using (var s = typeof(Changelog).Assembly.GetManifestResourceStream("CHANGELOG.md"))
                using (var r = new StreamReader(s))
                {
                    string line;
                    while ((line = r.ReadLine()) != null)
                    {
                        if (line.StartsWith("## ")) list.Add(new Entry { Title = line.Substring(3).Trim() });
                        else if (list.Count > 0 && line.Trim().StartsWith("- "))
                        {
                            bool sub = line.StartsWith("  ");
                            string text = Regex.Replace(line.Trim().Substring(2), @"\*\*(.+?)\*\*", "<b>$1</b>");
                            list[list.Count - 1].Lines.Add((sub ? "      ◦ " : "• ") + text);
                        }
                    }
                }
            }
            catch (System.Exception e) { Plugin.Log.LogWarning("[changelog] " + e.Message); }
            return list;
        }

        public static void MarkSeen() { Seen = Plugin.Version; Plugin.SaveNewsSeen(Seen); }
    }
}
