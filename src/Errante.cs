using HarmonyLib;

namespace MoonlighterCoop
{
    /// El Errante (el fantasma que sale si os quedáis demasiado en un piso): lo decide solo el anfitrión.
    /// Antes cada jugador tenía su propio reloj y al invitado le salía por su cuenta, a destiempo.
    /// Ahora el reloj del invitado no cuenta; cuando al anfitrión le sale el aviso o el Errante, se lo
    /// manda y al invitado le sale a la vez (en su copia de la sala, persiguiéndole a él).
    /// En la carrera por separado cada uno juega su piso, así que cada uno sigue con su propio reloj.
    public static class Errante
    {
        public const byte Aviso = 1, Sale = 2;

        /// Lo está poniendo el mensaje del anfitrión (no bloquearlo).
        public static bool Aplicando;

        /// El reloj del invitado no cuenta.
        public static bool Bloqueado => Net.Juego && Session.IsGuest && !Aplicando && !Race.Separate && !Session.TutorialActivo;

        /// El anfitrión avisa a los demás.
        public static bool Avisar => Net.Juego && Session.IsHost && !Race.Separate && !Session.TutorialActivo;

        public static bool Hay(DungeonManager dm) => dm != null && Traverse.Create(dm).Field("wanderer").GetValue() as UnityEngine.Object != null;

        public static void OnMsg(byte[] data)
        {
            var dm = DungeonManager.Instance;
            var gm = GameManager.Instance;
            if (dm == null || gm == null || !gm.IsDungeonSceneLoaded() || data.Length == 0) return;
            Aplicando = true;
            try
            {
                if (data[0] == Aviso) dm.SpawnWandererFeedback();
                else if (data[0] == Sale && !Hay(dm))
                {
                    if (dm.wandererPrefab == null) dm.wandererPrefab = EnemiesPrefabRegister.GetPrefabByName("Wanderer");
                    Traverse.Create(dm).Field("loadWanderer").SetValue(true);
                    dm.SpawnWanderer();
                    // Si en el invitado no se cumplía algo (sala de inicio, fuente...), sale igual: ya lo decidió el anfitrión
                    dm.CancelInvoke("SpawnWanderer");
                    if (!Hay(dm)) dm.ForceSpawnWanderer();
                    Plugin.Log.LogInfo("[errante] sale porque le ha salido al anfitrión");
                }
            }
            catch (System.Exception e) { Plugin.Log.LogWarning("[errante] " + e.Message); }
            finally { Aplicando = false; }
        }
    }

    [HarmonyPatch(typeof(DungeonManager), nameof(DungeonManager.SpawnWanderer))]
    static class ErranteSale
    {
        private static bool _noHabia;

        static bool Prefix(DungeonManager __instance)
        {
            if (Errante.Bloqueado) { __instance.CancelInvoke("SpawnWanderer"); return false; }
            _noHabia = !Errante.Hay(__instance);
            return true;
        }

        static void Postfix(DungeonManager __instance)
        {
            if (Errante.Avisar && _noHabia && Errante.Hay(__instance)) Net.Send(Msg.Wanderer, new[] { Errante.Sale });
        }
    }

    [HarmonyPatch(typeof(DungeonManager), nameof(DungeonManager.SpawnWandererFeedback))]
    static class ErranteAviso
    {
        static bool Prefix() => !Errante.Bloqueado;

        static void Postfix()
        {
            if (Errante.Avisar) Net.Send(Msg.Wanderer, new[] { Errante.Aviso });
        }
    }
}
