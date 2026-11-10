namespace MoonlighterCoop
{
    /// El nombre con el que te ven los demás: el que escribes en el menú (F2 / Coop) si has puesto uno,
    /// si no el de Steam, y si no hay Steam (móvil, o Steam cerrado) el de siempre ("Jugador 2"...).
    public static class Nombres
    {
        /// El que ha escrito el jugador ("" si ninguno). Se guarda en la configuración (Cooperativo/Nombre).
        public static string Propio = "";

        public static string Mio(string porDefecto)
        {
            if (!string.IsNullOrEmpty(Propio)) return Propio;
            if (SteamCoop.Available) return SteamCoop.MyName;
            return porDefecto;
        }

        public static void Cambiar(string nombre)
        {
            Propio = (nombre ?? "").Trim();
            if (Propio.Length > 20) Propio = Propio.Substring(0, 20);
            Plugin.SaveName(Propio);
        }
    }
}
