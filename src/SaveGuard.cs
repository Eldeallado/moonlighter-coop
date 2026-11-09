using System;
using System.Collections.Generic;
using FullSerializer;
using FullSerializer.Internal;

namespace MoonlighterCoop
{
    /// A guest loads the save the host sends. The game's serializer can be told, inside the data, to build objects
    /// of any type ("$type"), so a malicious host could try to make the guest build something it should not.
    /// Before loading, every "$type" in the data must be one of the game's own types (or a plain list/dictionary of
    /// them); anything else, or an oversized save, is refused.
    public static class SaveGuard
    {
        public const int MaxSaveBytes = 4 * 1024 * 1024;
        private static readonly HashSet<string> GameAssemblies = new HashSet<string> { "Assembly-CSharp", "Assembly-CSharp-firstpass" };

        public static bool IsSafe(byte[] json, out string why)
        {
            why = "";
            if (json == null || json.Length == 0) { why = "empty"; return false; }
            if (json.Length > MaxSaveBytes) { why = $"too big ({json.Length} bytes)"; return false; }
            fsData data;
            try
            {
                var r = fsJsonParser.Parse(System.Text.Encoding.UTF8.GetString(json), out data);
                if (r.Failed) { why = "not valid JSON"; return false; }
            }
            catch (Exception e) { why = "unreadable: " + e.Message; return false; }
            return Walk(data, 0, ref why);
        }

        private static bool Walk(fsData d, int depth, ref string why)
        {
            if (depth > 64) { why = "nested too deep"; return false; }
            if (d.IsList) { foreach (var x in d.AsList) if (!Walk(x, depth + 1, ref why)) return false; return true; }
            if (!d.IsDictionary) return true;
            foreach (var kv in d.AsDictionary)
            {
                if (kv.Key == "$type")
                {
                    if (!kv.Value.IsString || !Allowed(kv.Value.AsString)) { why = "type not allowed: " + (kv.Value.IsString ? kv.Value.AsString : "?"); return false; }
                    continue;
                }
                if (!Walk(kv.Value, depth + 1, ref why)) return false;
            }
            return true;
        }

        private static bool Allowed(string name)
        {
            Type t;
            try { t = fsTypeCache.GetType(name); } catch { return false; }
            return t != null && Allowed(t, 0);
        }

        private static bool Allowed(Type t, int depth)
        {
            if (depth > 8) return false;
            if (t.IsArray) return Allowed(t.GetElementType(), depth + 1);
            if (t.IsPrimitive || t.IsEnum && GameAssemblies.Contains(t.Assembly.GetName().Name) || t == typeof(string) || t == typeof(decimal) || t == typeof(DateTime)) return true;
            if (GameAssemblies.Contains(t.Assembly.GetName().Name))
                return !typeof(Delegate).IsAssignableFrom(t);
            // Plain collections of allowed types.
            if (t.IsGenericType && t.Namespace == "System.Collections.Generic")
            {
                foreach (var a in t.GetGenericArguments()) if (!Allowed(a, depth + 1)) return false;
                return true;
            }
            // Unity value types the game stores (positions, colours).
            if (t.IsValueType && t.Namespace == "UnityEngine") return true;
            return false;
        }
    }
}
