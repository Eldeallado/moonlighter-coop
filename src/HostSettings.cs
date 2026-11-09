namespace MoonlighterCoop
{
    /// Rules of the game the host chooses in F2 and everybody plays by (sent with Msg.Settings).
    public static class HostSettings
    {
        public static bool Roles;          // each player picks a role when entering a dungeon
        public static bool BossWait = true; // the boss door waits until everyone is ready
        public static bool ShopContest;    // the shop is a contest: each one's shelves, ranking at night
        public static bool OneHeart;       // if anyone falls, the run is over for everybody
        public static bool Spectator = true; // joining mid-floor: watch as a ghost until the next floor
        public static bool HardRevive;     // ghosts only come back at a fountain or with a potion, and with less health
        public static bool Race;           // normal floors: each plays his own copy, all wait at the way down (Race.cs)

        public static readonly string[] Names = { "Roles al entrar en la mazmorra", "Esperar a todos en la puerta del jefe", "Tienda competitiva", "Un solo corazón (si cae uno, acaba la expedición)", "Unirse a mitad de piso como espectador", "Revivir difícil (solo fuente o poción, con poca vida)", "Carrera: cada uno su copia del piso, os esperáis en la puerta" };
        public static bool Get(int i) => i == 0 ? Roles : i == 1 ? BossWait : i == 2 ? ShopContest : i == 3 ? OneHeart : i == 4 ? Spectator : i == 5 ? HardRevive : Race;
        public static int Count => Names.Length;

        public static int Bits => (Roles ? 1 : 0) | (BossWait ? 2 : 0) | (ShopContest ? 4 : 0) | (OneHeart ? 8 : 0) | (Spectator ? 16 : 0) | (HardRevive ? 32 : 0) | (Race ? 64 : 0);
        public static void FromBits(int b) { Roles = (b & 1) != 0; BossWait = (b & 2) != 0; ShopContest = (b & 4) != 0; OneHeart = (b & 8) != 0; Spectator = (b & 16) != 0; HardRevive = (b & 32) != 0; Race = (b & 64) != 0; }

        public static void Toggle(int i)
        {
            if (!Session.IsHost && Net.Connected) return;
            FromBits(Bits ^ (1 << i));
            Plugin.SaveHostSettings(Bits);
            Send();
        }

        public static void Send() { if (Session.IsHost && Net.Connected) Net.Send(Msg.Settings, new[] { (byte)Bits }); }

        public static void OnSettings(byte[] data)
        {
            if (Session.IsHost || data.Length < 1) return;
            int before = Bits;
            FromBits(data[0]);
            if (before != Bits) Plugin.Log.LogInfo($"[settings] host rules: {Bits}");
        }
    }
}
