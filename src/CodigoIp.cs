using System;
using System.Net;
using System.Net.Sockets;

namespace MoonlighterCoop
{
    /// Código para unirse sin Steam (el móvil no tiene Steam): lleva dentro la IP del anfitrión.
    ///  - "IP" + 4 letras: red de casa (192.168.x.y) con el puerto normal. Es lo habitual.
    ///  - "IP" + 10 letras: cualquier IP y puerto (para jugar desde fuera hay que abrir el puerto en el router).
    /// Mismas letras que los códigos de Steam (sin I, O, 0 ni 1 para no confundirse).
    public static class CodigoIp
    {
        private const string Letras = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

        public static bool EsCodigoIp(string code) => !string.IsNullOrEmpty(code) && code.Trim().ToUpperInvariant().StartsWith("IP");

        /// Dirección por la que se llega a este PC desde internet ("ip:puerto" o "nombre:puerto"), p. ej. la de un
        /// túnel: con CGNAT (muchas fibras) no se puede abrir un puerto en el router. Vacía = solo la wifi de casa.
        /// Se guarda en la configuración (Cooperativo/DireccionInternet).
        public static string Internet = "";
        private static string _resuelta, _de;

        /// El código que da el anfitrión: con dirección de internet vale desde cualquier sitio; si no, el de casa.
        public static string DelAnfitrion()
        {
            if (TryInternet(out var ip, out var port)) return Crear(ip, port);
            return Crear(IpLocal(), Plugin.Port);
        }

        public static bool HayInternet => TryInternet(out _, out _);

        private static bool TryInternet(out string ip, out int port)
        {
            ip = null; port = 0;
            string s = (Internet ?? "").Trim();
            int c = s.LastIndexOf(':');
            if (c <= 0 || !int.TryParse(s.Substring(c + 1), out port) || port <= 0 || port > 65535) return false;
            string host = s.Substring(0, c);
            if (_de != host)
            {
                // Un nombre (como los de los túneles) se convierte a su IPv4 una vez
                _de = host; _resuelta = null;
                if (IPAddress.TryParse(host, out var a) && a.AddressFamily == AddressFamily.InterNetwork) _resuelta = host;
                else
                    try { foreach (var x in Dns.GetHostAddresses(host)) if (x.AddressFamily == AddressFamily.InterNetwork) { _resuelta = x.ToString(); break; } }
                    catch { }
            }
            ip = _resuelta;
            return ip != null;
        }

        public static void CambiarInternet(string s)
        {
            Internet = (s ?? "").Trim();
            Plugin.SaveInternet(Internet);
        }

        public static string Crear(string ip, int port)
        {
            IPAddress a;
            if (!IPAddress.TryParse(ip, out a) || a.AddressFamily != AddressFamily.InterNetwork) return null;
            byte[] b = a.GetAddressBytes();
            if (b[0] == 192 && b[1] == 168 && port == Plugin.DefaultPort)
                return "IP" + Base32(((ulong)b[2] << 8) | b[3], 4);
            ulong v = 0;
            foreach (byte x in b) v = (v << 8) | x;
            v = (v << 16) | (ushort)port;
            return "IP" + Base32(v, 10);
        }

        public static bool Leer(string code, out string ip, out int port)
        {
            ip = null; port = Plugin.DefaultPort;
            if (!EsCodigoIp(code)) return false;
            string s = code.Trim().ToUpperInvariant().Substring(2).Replace("-", "").Replace(" ", "");
            ulong v;
            if (!DeBase32(s, out v)) return false;
            if (s.Length == 4)
            {
                ip = $"192.168.{(v >> 8) & 255}.{v & 255}";
                return true;
            }
            if (s.Length == 10)
            {
                port = (int)(v & 0xFFFF);
                v >>= 16;
                ip = $"{(v >> 24) & 255}.{(v >> 16) & 255}.{(v >> 8) & 255}.{v & 255}";
                return true;
            }
            return false;
        }

        private static string Base32(ulong v, int n)
        {
            var c = new char[n];
            for (int i = n - 1; i >= 0; i--) { c[i] = Letras[(int)(v & 31)]; v >>= 5; }
            return new string(c);
        }

        private static bool DeBase32(string s, out ulong v)
        {
            v = 0;
            foreach (char ch in s)
            {
                int i = Letras.IndexOf(ch);
                if (i < 0) return false;
                v = (v << 5) | (uint)i;
            }
            return true;
        }

        /// IP de este aparato en la red de casa. Abre un socket UDP hacia fuera sin enviar nada:
        /// así se sabe por qué red se sale (funciona también en Android, donde el nombre del equipo no sirve).
        public static string IpLocal()
        {
            try
            {
                using (var s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
                {
                    s.Connect("8.8.8.8", 53);
                    var ep = s.LocalEndPoint as IPEndPoint;
                    if (ep != null && !IPAddress.IsLoopback(ep.Address)) return ep.Address.ToString();
                }
            }
            catch { }
            try
            {
                foreach (var a in Dns.GetHostEntry(Dns.GetHostName()).AddressList)
                    if (a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a)) return a.ToString();
            }
            catch { }
            return "?";
        }
    }
}
