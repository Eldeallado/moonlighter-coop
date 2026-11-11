using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace MoonlighterCoop
{
    /// Adaptador TCP ↔ UDP para jugar desde fuera de casa sin abrir puertos ni usar túneles.
    /// Con CGNAT (muchas fibras) nadie puede conectarse por TCP al PC desde internet, pero por UDP los dos lados
    /// pueden "perforar" el NAT: cada uno manda paquetes al otro y los routers los toman por respuestas.
    /// El mod sigue hablando TCP como siempre; esto lo lleva por UDP por el camino:
    ///  - Anfitrión: escucha en UDP; por cada jugador de fuera abre una conexión TCP a su propio servidor
    ///    (127.0.0.1) y pasa los datos de un lado a otro.
    ///  - Invitado: el juego se conecta a 127.0.0.1 (aquí) y esto lo manda por UDP al anfitrión.
    /// Por UDP los paquetes se pueden perder o desordenar: cada trozo va numerado, se confirma y se reenvía
    /// hasta que llega, y se entrega en orden. Con latidos cada 2 s para que el CGNAT no cierre el camino.
    /// La dirección pública se pregunta a un servidor STUN público (solo esa pregunta; la partida va directa).
    /// Código «PU»: IP pública + puerto + IP de casa (para entrar directo si estáis en la misma wifi) + clave.
    /// Código «RE» (de vuelta): si el router del anfitrión no deja pasar al invitado, el invitado se lo manda
    /// y el anfitrión le "llama" primero.
    public static class Puente
    {
        public const int PuertoUdp = 7796;
        private const byte Hello = 1, Welcome = 2, Data = 3, Ack = 4, Ping = 5, Close = 6, Punch = 7, Eco = 8;
        private const int Trozo = 1000, Ventana = 256, MaxSesiones = 8;
        private static readonly string[] Stun = { "stun.l.google.com:19302", "stun1.l.google.com:19302", "stun.cloudflare.com:3478" };

        private class Sesion
        {
            public uint Id;
            public IPEndPoint Remoto;
            public TcpClient Tcp;
            public NetworkStream Flujo;
            public bool Abierta, Cerrada;
            public uint SigEnvio, Esperado;
            public readonly SortedDictionary<uint, Pendiente> SinAck = new SortedDictionary<uint, Pendiente>();
            public readonly Dictionary<uint, byte[]> Fuera = new Dictionary<uint, byte[]>();
            public DateTime Recibido = DateTime.UtcNow, Enviado = DateTime.UtcNow, Inicio = DateTime.UtcNow, Hola;
            public readonly object L = new object();
            public int PuertoTcpLocal;
        }

        private class Pendiente { public byte[] Paquete; public DateTime Ultimo; public int Veces; }

        private static UdpClient _udp;
        private static volatile bool _vivo;
        private static bool _anfitrion;
        private static uint _secreto;
        private static IPEndPoint _destino;
        private static TcpListener _local;
        private static readonly Dictionary<uint, Sesion> _sesiones = new Dictionary<uint, Sesion>();
        private static readonly object _l = new object();
        private static readonly Dictionary<IPEndPoint, DateTime> _llamar = new Dictionary<IPEndPoint, DateTime>();
        private static readonly System.Random _azar = new System.Random();
        private static DateTime _proximoStun;
        private static int _stun;

        /// Cómo nos ven desde internet (lo dice el STUN). Null hasta que contesta.
        public static IPEndPoint Publica;
        /// Para mostrar en el menú.
        public static string Estado = "";
        public static bool Activo => _vivo;
        /// Pruebas: fracción de paquetes que se tiran a propósito (0 = ninguno).
        public static double PerdidaPrueba;
        public static int Reenviados, Recibidos, Enviados;

        // ---------- códigos ----------

        /// Código del anfitrión para entrar desde cualquier sitio (null mientras no sabe su dirección pública).
        public static string CodigoFuera
        {
            get
            {
                var p = Publica;
                if (!_anfitrion || !_vivo || p == null) return null;
                return CodigoPara(p, true);
            }
        }

        /// El mismo código, pero apuntando a otra dirección (pruebas en el mismo PC).
        public static string CodigoPara(IPEndPoint p, bool conCasa)
        {
            byte[] b = new byte[11];
            Array.Copy(p.Address.GetAddressBytes(), 0, b, 0, 4);
            b[4] = (byte)(p.Port >> 8); b[5] = (byte)p.Port;
            if (conCasa)
            {
                IPAddress casa;
                if (IPAddress.TryParse(CodigoIp.IpLocal(), out casa))
                {
                    byte[] c = casa.GetAddressBytes();
                    if (c[0] == 192 && c[1] == 168) { b[6] = c[2]; b[7] = c[3]; }
                }
            }
            b[8] = (byte)(_secreto >> 16); b[9] = (byte)(_secreto >> 8); b[10] = (byte)_secreto;
            return "PU" + Base32.De(b);
        }

        public static bool EsCodigo(string code) => !string.IsNullOrEmpty(code) && code.Trim().ToUpperInvariant().StartsWith("PU");

        /// Lee un código «PU»: a dónde mandar, la IP de casa del anfitrión ("" si no la lleva) y la clave.
        public static bool Leer(string code, out IPEndPoint destino, out string casa, out uint secreto)
        {
            destino = null; casa = ""; secreto = 0;
            if (!EsCodigo(code)) return false;
            byte[] b = Base32.A(code.Trim().ToUpperInvariant().Substring(2).Replace("-", "").Replace(" ", ""), 11);
            if (b == null) return false;
            destino = new IPEndPoint(new IPAddress(new[] { b[0], b[1], b[2], b[3] }), (b[4] << 8) | b[5]);
            if (b[6] != 0 || b[7] != 0) casa = "192.168." + b[6] + "." + b[7];
            secreto = (uint)((b[8] << 16) | (b[9] << 8) | b[10]);
            return destino.Port > 0;
        }

        /// Invitado: su código de vuelta («RE»), por si el anfitrión tiene que llamarle primero.
        public static string CodigoRespuesta
        {
            get
            {
                var p = Publica;
                if (_anfitrion || !_vivo || p == null) return null;
                byte[] b = new byte[6];
                Array.Copy(p.Address.GetAddressBytes(), 0, b, 0, 4);
                b[4] = (byte)(p.Port >> 8); b[5] = (byte)p.Port;
                return "RE" + Base32.De(b);
            }
        }

        /// Anfitrión: el invitado le ha pasado su código de vuelta; durante 30 s se le "llama" para abrir el camino.
        public static bool Responder(string code)
        {
            if (string.IsNullOrEmpty(code) || !code.Trim().ToUpperInvariant().StartsWith("RE")) return false;
            byte[] b = Base32.A(code.Trim().ToUpperInvariant().Substring(2).Replace("-", "").Replace(" ", ""), 6);
            if (b == null) return false;
            var ep = new IPEndPoint(new IPAddress(new[] { b[0], b[1], b[2], b[3] }), (b[4] << 8) | b[5]);
            lock (_l) _llamar[ep] = DateTime.UtcNow.AddSeconds(30);
            Plugin.Log.LogInfo("[puente] llamando a " + ep);
            return true;
        }

        /// Anfitrión: IP real ("ip") del jugador que entra por el puente con esta conexión local, o null.
        public static string RemotoDe(string remotoTcp)
        {
            if (string.IsNullOrEmpty(remotoTcp) || !remotoTcp.StartsWith("127.0.0.1:")) return null;
            int puerto;
            if (!int.TryParse(remotoTcp.Substring(10), out puerto)) return null;
            lock (_l) foreach (var s in _sesiones.Values) if (s.PuertoTcpLocal == puerto) return s.Remoto.Address.ToString();
            return null;
        }

        /// Invitado: ¿está el anfitrión de este código en la misma wifi? Le pregunta por UDP con la clave
        /// (como mucho medio segundo). Así no se confunde con otro aparato que tenga la misma IP en otra casa.
        public static bool EnCasa(string ip, uint clave)
        {
            try
            {
                using (var u = new UdpClient(0))
                {
                    u.Client.ReceiveTimeout = 500;
                    byte[] q = Cabecera(Eco, 0, 3);
                    q[7] = (byte)(clave >> 16); q[8] = (byte)(clave >> 8); q[9] = (byte)clave;
                    var a = new IPEndPoint(IPAddress.Parse(ip), PuertoUdp);
                    u.Send(q, q.Length, a);
                    IPEndPoint de = new IPEndPoint(IPAddress.Any, 0);
                    byte[] r = u.Receive(ref de);
                    return r.Length >= 3 && r[0] == 'M' && r[1] == 'B' && r[2] == Eco;
                }
            }
            catch { return false; }
        }

        // ---------- arrancar y parar ----------

        /// Anfitrión: empieza a escuchar en UDP y pregunta su dirección pública.
        public static void Abrir()
        {
            if (_vivo && _anfitrion) return;
            Detener();
            _anfitrion = true;
            _secreto = (uint)_azar.Next(1, 1 << 24);
            try { _udp = Socket(PuertoUdp); }
            catch { _udp = Socket(0); }   // el puerto ocupado (otra copia del juego): otro cualquiera
            Estado = "Preguntando la dirección de internet…";
            Arrancar();
            Plugin.Log.LogInfo("[puente] anfitrión en UDP " + ((IPEndPoint)_udp.Client.LocalEndPoint).Port);
        }

        /// Invitado: prepara la entrada local (127.0.0.1) y devuelve su puerto; el juego se conecta ahí.
        public static int Unirse(IPEndPoint destino, uint secreto)
        {
            Detener();
            _anfitrion = false;
            _destino = destino;
            _secreto = secreto;
            _udp = Socket(0);
            _local = new TcpListener(IPAddress.Loopback, 0);
            _local.Start();
            int puerto = ((IPEndPoint)_local.LocalEndpoint).Port;
            Estado = "Conectando por internet…";
            Arrancar();
            var aceptar = new Thread(Aceptar) { IsBackground = true, Name = "puente-aceptar" };
            aceptar.Start();
            Plugin.Log.LogInfo($"[puente] invitado hacia {destino}, entrada local {puerto}");
            return puerto;
        }

        public static void Detener()
        {
            if (!_vivo && _udp == null) return;
            _vivo = false;
            List<Sesion> todas;
            lock (_l) { todas = new List<Sesion>(_sesiones.Values); _sesiones.Clear(); _llamar.Clear(); }
            foreach (var s in todas) Cerrar(s, true);
            try { _local?.Stop(); } catch { }
            try { _udp?.Close(); } catch { }
            _local = null; _udp = null; Publica = null; Estado = "";
            Plugin.Log.LogInfo("[puente] parado");
        }

        private static UdpClient Socket(int puerto)
        {
            var u = new UdpClient(new IPEndPoint(IPAddress.Any, puerto));
            // Windows: que un "puerto inalcanzable" de vuelta no rompa la escucha
            try { u.Client.IOControl(-1744830452, new byte[] { 0, 0, 0, 0 }, null); } catch { }
            return u;
        }

        private static void Arrancar()
        {
            _vivo = true;
            _proximoStun = DateTime.MinValue;
            new Thread(Escuchar) { IsBackground = true, Name = "puente-udp" }.Start();
            new Thread(Reloj) { IsBackground = true, Name = "puente-reloj" }.Start();
        }

        // ---------- hilos ----------

        private static void Escuchar()
        {
            var u = _udp;
            while (_vivo && u == _udp)
            {
                byte[] p; IPEndPoint de = new IPEndPoint(IPAddress.Any, 0);
                try { p = u.Receive(ref de); }
                catch (SocketException) { if (!_vivo) return; continue; }
                catch { return; }
                if (PerdidaPrueba > 0 && _azar.NextDouble() < PerdidaPrueba) continue;
                try { Llega(p, de); } catch (Exception e) { Plugin.Log.LogWarning("[puente] " + e.Message); }
            }
        }

        private static void Reloj()
        {
            var u = _udp;
            while (_vivo && u == _udp)
            {
                var ahora = DateTime.UtcNow;
                try
                {
                    // La dirección pública, y cada 20 s otra vez (de paso mantiene abierto el camino en el CGNAT)
                    if (ahora >= _proximoStun) { PreguntarStun(); _proximoStun = ahora.AddSeconds(Publica == null ? 2 : 20); }
                    List<Sesion> todas;
                    lock (_l) todas = new List<Sesion>(_sesiones.Values);
                    foreach (var s in todas) Atender(s, ahora);
                    if (_anfitrion)
                    {
                        List<IPEndPoint> llamar = new List<IPEndPoint>();
                        lock (_l)
                        {
                            foreach (var kv in new List<KeyValuePair<IPEndPoint, DateTime>>(_llamar))
                                if (kv.Value < ahora) _llamar.Remove(kv.Key); else llamar.Add(kv.Key);
                        }
                        foreach (var ep in llamar) Mandar(new byte[] { (byte)'M', (byte)'B', Punch }, ep);
                    }
                    else
                    {
                        // Invitado: estado para el menú
                        bool alguna = false, abierta = false;
                        foreach (var s in todas) { alguna = true; abierta |= s.Abierta; }
                        if (abierta) Estado = "Conectado por internet";
                        else if (alguna && todas[0].Inicio.AddSeconds(8) < ahora && CodigoRespuesta != null)
                            Estado = "No contesta: pásale al anfitrión tu código " + CodigoRespuesta + " (lo pega en «Pegar respuesta»)";
                    }
                }
                catch (Exception e) { Plugin.Log.LogWarning("[puente] reloj: " + e.Message); }
                Thread.Sleep(20);
            }
        }

        private static void Atender(Sesion s, DateTime ahora)
        {
            if (s.Cerrada) return;
            if (!s.Abierta)
            {
                // Invitado: "hola" cada 250 ms hasta que el anfitrión conteste; a los 60 s se rinde
                if (!_anfitrion && (ahora - s.Hola).TotalMilliseconds >= 250)
                {
                    s.Hola = ahora;
                    byte[] h = Cabecera(Hello, s.Id, 3);
                    h[7] = (byte)(_secreto >> 16); h[8] = (byte)(_secreto >> 8); h[9] = (byte)_secreto;
                    Mandar(h, s.Remoto);
                }
                if ((ahora - s.Inicio).TotalSeconds > 60) { Estado = "No se pudo conectar por internet"; Cerrar(s, true); }
                return;
            }
            List<byte[]> otraVez = new List<byte[]>();
            lock (s.L)
            {
                int n = 0;
                foreach (var kv in s.SinAck)
                {
                    var pe = kv.Value;
                    double rto = Math.Min(2000, 250 * Math.Pow(1.5, Math.Min(pe.Veces, 6)));
                    if ((ahora - pe.Ultimo).TotalMilliseconds >= rto) { pe.Ultimo = ahora; pe.Veces++; otraVez.Add(pe.Paquete); if (pe.Veces > 1) Reenviados++; }
                    if (++n >= 64) break;
                }
            }
            foreach (var p in otraVez) Mandar(p, s.Remoto);
            if (otraVez.Count > 0) s.Enviado = ahora;
            if ((ahora - s.Enviado).TotalSeconds >= 2) { s.Enviado = ahora; Mandar(Cabecera(Ping, s.Id, 0), s.Remoto); }
            if ((ahora - s.Recibido).TotalSeconds > 30) { Plugin.Log.LogInfo("[puente] sesión " + s.Id + " sin noticias: se cierra"); Cerrar(s, true); }
        }

        /// Invitado: cada conexión del juego a 127.0.0.1 es una sesión nueva hacia el anfitrión.
        private static void Aceptar()
        {
            var l = _local;
            while (_vivo && l == _local)
            {
                TcpClient c;
                try { c = l.AcceptTcpClient(); } catch { return; }
                var s = new Sesion { Id = (uint)_azar.Next(1, int.MaxValue), Remoto = _destino, Tcp = c, Flujo = c.GetStream() };
                c.NoDelay = true;
                lock (_l) _sesiones[s.Id] = s;
                Leer(s);
            }
        }

        /// Lo que escribe el juego por TCP sale por UDP en trozos numerados.
        private static void Leer(Sesion s)
        {
            new Thread(() =>
            {
                byte[] buf = new byte[Trozo];
                try
                {
                    while (!s.Cerrada)
                    {
                        int n = s.Flujo.Read(buf, 0, buf.Length);
                        if (n <= 0) break;
                        byte[] p = Cabecera(Data, s.Id, 4 + n);
                        lock (s.L)
                        {
                            while (s.SinAck.Count >= Ventana && !s.Cerrada) Monitor.Wait(s.L, 200);
                            uint seq = s.SigEnvio++;
                            Escribir32(p, 7, seq);
                            Array.Copy(buf, 0, p, 11, n);
                            s.SinAck[seq] = new Pendiente { Paquete = p, Ultimo = DateTime.MinValue };
                        }
                        // Sale ya (el reloj solo reenvía lo que no se confirma); sin abrir aún, espera al reloj
                        if (s.Abierta) Enviar(s);
                    }
                }
                catch { }
                Cerrar(s, true);
            }) { IsBackground = true, Name = "puente-tcp" }.Start();
        }

        private static void Enviar(Sesion s)
        {
            List<byte[]> ya = new List<byte[]>();
            var ahora = DateTime.UtcNow;
            lock (s.L)
                foreach (var kv in s.SinAck)
                    if (kv.Value.Veces == 0) { kv.Value.Veces = 1; kv.Value.Ultimo = ahora; ya.Add(kv.Value.Paquete); }
            foreach (var p in ya) Mandar(p, s.Remoto);
            s.Enviado = ahora;
        }

        // ---------- lo que llega ----------

        private static void Llega(byte[] p, IPEndPoint de)
        {
            // Respuesta del STUN
            if (p.Length >= 20 && p[0] == 0x01 && p[1] == 0x01) { LeerStun(p); return; }
            if (p.Length < 3 || p[0] != 'M' || p[1] != 'B') return;
            byte tipo = p[2];
            if (tipo == Punch) return;
            if (p.Length < 7) return;
            uint id = Leer32(p, 3);
            Sesion s;
            lock (_l) _sesiones.TryGetValue(id, out s);
            Recibidos++;

            if (tipo == Eco)
            {
                if (_anfitrion && p.Length >= 10 && (uint)((p[7] << 16) | (p[8] << 8) | p[9]) == _secreto) Mandar(Cabecera(Eco, 0, 0), de);
                return;
            }
            if (tipo == Hello)
            {
                if (!_anfitrion || p.Length < 10) return;
                uint clave = (uint)((p[7] << 16) | (p[8] << 8) | p[9]);
                if (clave != _secreto) return;   // sin la clave del código no se contesta nada
                if (s == null)
                {
                    lock (_l) if (_sesiones.Count >= MaxSesiones) return;
                    s = NuevaEnAnfitrion(id, de);
                    if (s == null) return;
                }
                s.Remoto = de;
                s.Recibido = DateTime.UtcNow;
                Mandar(Cabecera(Welcome, id, 0), de);
                return;
            }
            if (s == null || s.Cerrada) return;
            // La clave ya se comprobó en el hola: lo demás tiene que venir del mismo sitio
            if (_anfitrion && !s.Remoto.Equals(de)) return;
            s.Recibido = DateTime.UtcNow;
            switch (tipo)
            {
                case Welcome:
                    if (!s.Abierta) { s.Abierta = true; Plugin.Log.LogInfo("[puente] conectado con " + de); Enviar(s); }
                    break;
                case Data:
                    if (p.Length < 11) return;
                    Recibir(s, Leer32(p, 7), p);
                    break;
                case Ack:
                    if (p.Length < 11) return;
                    uint hasta = Leer32(p, 7);
                    lock (s.L)
                    {
                        var quitar = new List<uint>();
                        foreach (var k in s.SinAck.Keys) { if (k < hasta) quitar.Add(k); else break; }
                        foreach (var k in quitar) s.SinAck.Remove(k);
                        Monitor.PulseAll(s.L);
                    }
                    break;
                case Close:
                    Cerrar(s, false);
                    break;
            }
        }

        private static Sesion NuevaEnAnfitrion(uint id, IPEndPoint de)
        {
            try
            {
                var c = new TcpClient();
                c.Connect(IPAddress.Loopback, Plugin.Port);
                c.NoDelay = true;
                var s = new Sesion { Id = id, Remoto = de, Tcp = c, Flujo = c.GetStream(), Abierta = true, PuertoTcpLocal = ((IPEndPoint)c.Client.LocalEndPoint).Port };
                lock (_l) _sesiones[id] = s;
                Leer(s);
                Plugin.Log.LogInfo($"[puente] entra {de} (sesión {id})");
                return s;
            }
            catch (Exception e) { Plugin.Log.LogWarning("[puente] no se pudo abrir la partida para " + de + ": " + e.Message); return null; }
        }

        /// En orden al juego; lo que llega antes de tiempo espera; siempre se confirma hasta dónde va.
        private static void Recibir(Sesion s, uint seq, byte[] p)
        {
            lock (s.L)
            {
                if (seq == s.Esperado)
                {
                    if (!Entregar(s, p, 11, p.Length - 11)) return;
                    s.Esperado++;
                    byte[] sig;
                    while (s.Fuera.TryGetValue(s.Esperado, out sig))
                    {
                        s.Fuera.Remove(s.Esperado);
                        if (!Entregar(s, sig, 11, sig.Length - 11)) return;
                        s.Esperado++;
                    }
                }
                else if (seq > s.Esperado && seq < s.Esperado + Ventana * 2) s.Fuera[seq] = p;
            }
            byte[] a = Cabecera(Ack, s.Id, 4);
            Escribir32(a, 7, s.Esperado);
            Mandar(a, s.Remoto);
        }

        private static bool Entregar(Sesion s, byte[] p, int desde, int n)
        {
            try { s.Flujo.Write(p, desde, n); return true; }
            catch { Cerrar(s, true); return false; }
        }

        private static void Cerrar(Sesion s, bool avisar)
        {
            if (s.Cerrada) return;
            s.Cerrada = true;
            lock (s.L) Monitor.PulseAll(s.L);
            if (avisar && s.Remoto != null) for (int i = 0; i < 3; i++) Mandar(Cabecera(Close, s.Id, 0), s.Remoto);
            try { s.Tcp?.Close(); } catch { }
            lock (_l) _sesiones.Remove(s.Id);
        }

        // ---------- STUN ----------

        private static void PreguntarStun()
        {
            string[] hp = Stun[_stun % Stun.Length].Split(':');
            _stun++;
            try
            {
                IPAddress ip = null;
                foreach (var a in Dns.GetHostAddresses(hp[0])) if (a.AddressFamily == AddressFamily.InterNetwork) { ip = a; break; }
                if (ip == null) return;
                byte[] q = new byte[20];
                q[1] = 1;                                          // Binding request
                q[4] = 0x21; q[5] = 0x12; q[6] = 0xA4; q[7] = 0x42;   // magic cookie
                for (int i = 8; i < 20; i++) q[i] = (byte)_azar.Next(256);
                Mandar(q, new IPEndPoint(ip, int.Parse(hp[1])));
            }
            catch { }
        }

        private static void LeerStun(byte[] r)
        {
            int i = 20;
            while (i + 4 <= r.Length)
            {
                int t = (r[i] << 8) | r[i + 1], l = (r[i + 2] << 8) | r[i + 3];
                if ((t == 0x20 || t == 0x01) && l >= 8 && i + 12 <= r.Length && r[i + 5] == 1)
                {
                    int puerto = (r[i + 6] << 8) | r[i + 7];
                    byte[] a = { r[i + 8], r[i + 9], r[i + 10], r[i + 11] };
                    if (t == 0x20) { puerto ^= 0x2112; a[0] ^= 0x21; a[1] ^= 0x12; a[2] ^= 0xA4; a[3] ^= 0x42; }
                    var nueva = new IPEndPoint(new IPAddress(a), puerto);
                    if (Publica == null || !Publica.Equals(nueva)) Plugin.Log.LogInfo("[puente] desde internet nos ven en " + nueva);
                    Publica = nueva;
                    if (_anfitrion) Estado = "Listo para entrar desde fuera";
                    return;
                }
                i += 4 + l + ((4 - l % 4) % 4);
            }
        }

        // ---------- paquetes ----------

        private static byte[] Cabecera(byte tipo, uint id, int extra)
        {
            byte[] p = new byte[7 + extra];
            p[0] = (byte)'M'; p[1] = (byte)'B'; p[2] = tipo;
            Escribir32(p, 3, id);
            return p;
        }

        private static void Mandar(byte[] p, IPEndPoint a)
        {
            var u = _udp;
            if (u == null || a == null) return;
            if (PerdidaPrueba > 0 && _azar.NextDouble() < PerdidaPrueba) return;
            try { u.Send(p, p.Length, a); Enviados++; } catch { }
        }

        private static void Escribir32(byte[] p, int o, uint v) { p[o] = (byte)(v >> 24); p[o + 1] = (byte)(v >> 16); p[o + 2] = (byte)(v >> 8); p[o + 3] = (byte)v; }
        private static uint Leer32(byte[] p, int o) => (uint)((p[o] << 24) | (p[o + 1] << 16) | (p[o + 2] << 8) | p[o + 3]);
    }

    /// Letras de los códigos (sin I, O, 0 ni 1), para bytes de cualquier largo.
    public static class Base32
    {
        private const string Letras = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

        public static string De(byte[] b)
        {
            var sb = new System.Text.StringBuilder();
            int bits = 0, acc = 0;
            foreach (byte x in b)
            {
                acc = (acc << 8) | x; bits += 8;
                while (bits >= 5) { sb.Append(Letras[(acc >> (bits - 5)) & 31]); bits -= 5; }
            }
            if (bits > 0) sb.Append(Letras[(acc << (5 - bits)) & 31]);
            return sb.ToString();
        }

        public static byte[] A(string s, int bytes)
        {
            var r = new List<byte>();
            int bits = 0, acc = 0;
            foreach (char ch in s)
            {
                int i = Letras.IndexOf(ch);
                if (i < 0) return null;
                acc = ((acc << 5) | i) & 0xFFFF; bits += 5;
                if (bits >= 8) { r.Add((byte)(acc >> (bits - 8))); bits -= 8; }
            }
            return r.Count >= bytes ? r.GetRange(0, bytes).ToArray() : null;
        }
    }
}
