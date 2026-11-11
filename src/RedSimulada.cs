using System;
using System.Collections.Generic;
using System.Threading;

namespace MoonlighterCoop
{
    /// Solo para las pruebas: hace que la conexión entre dos copias en el mismo PC se comporte como internet o
    /// como Steam. Se aplica a lo que llega (así vale para cualquier conexión):
    ///  - lo "rápido" (poses y enemigos, ver SteamCoop.EsRapido) se puede perder (Perdida);
    ///  - todo llega con Retardo ms más hasta Variacion ms al azar; lo que tiene que llegar, en orden;
    ///  - cuenta las poses que Steam habría tirado si fueran por el canal rápido (Grandes, y la mayor).
    /// Se activa con -coopred perdida,retardo,variacion o con la orden "red" del driver de pruebas.
    public static class RedSimulada
    {
        public static bool Activa;
        public static double Perdida;
        public static int Retardo, Variacion;
        public static int Perdidos, Retenidos, Grandes, Mayor;

        private static readonly System.Random _azar = new System.Random();

        private class Pendiente { public DateTime Cuando; public ConcurrentQueueHolder Destino; public Msg Tipo; public byte[] Datos; }
        private class ConcurrentQueueHolder { public System.Collections.Concurrent.ConcurrentQueue<(Msg, byte[])> Q; }

        private static readonly List<Pendiente> _cola = new List<Pendiente>();
        private static readonly Dictionary<object, DateTime> _ultimoFiable = new Dictionary<object, DateTime>();
        private static Thread _hilo;

        public static void Poner(double perdida, int retardo, int variacion)
        {
            Perdida = perdida; Retardo = retardo; Variacion = variacion;
            Activa = perdida > 0 || retardo > 0 || variacion > 0;
            Perdidos = Retenidos = Grandes = Mayor = 0;
            Plugin.Log.LogInfo($"[red] simulada: pérdida {perdida:P0}, retardo {retardo} ms ± {variacion} ms");
        }

        public static string Estado() => $"activa={Activa} perdida={Perdida} retardo={Retardo} variacion={Variacion} perdidos={Perdidos} retenidos={Retenidos} grandes={Grandes} mayor={Mayor}";

        /// Lo llama el hilo que lee la conexión con cada mensaje. Devuelve false si se lo queda (o lo tira).
        public static bool Llega(object conexion, System.Collections.Concurrent.ConcurrentQueue<(Msg, byte[])> inbox, Msg tipo, byte[] datos)
        {
            int bytes = datos.Length + 1;
            if (tipo == Msg.Pose || tipo == Msg.Enemies)
            {
                if (bytes > Mayor) Mayor = bytes;
                if (bytes > SteamCoop.LimiteRapido) Grandes++;
            }
            if (!Activa) return true;
            bool rapido = SteamCoop.EsRapido(tipo, bytes);
            if (rapido && _azar.NextDouble() < Perdida) { Perdidos++; return false; }
            if (Retardo <= 0 && Variacion <= 0) return true;
            var cuando = DateTime.UtcNow.AddMilliseconds(Retardo + (Variacion > 0 ? _azar.Next(Variacion + 1) : 0));
            lock (_cola)
            {
                // Lo que tiene que llegar no se adelanta a lo anterior (como TCP o el canal seguro de Steam)
                if (!rapido)
                {
                    DateTime ult;
                    if (_ultimoFiable.TryGetValue(conexion, out ult) && cuando < ult) cuando = ult;
                    _ultimoFiable[conexion] = cuando;
                }
                _cola.Add(new Pendiente { Cuando = cuando, Destino = new ConcurrentQueueHolder { Q = inbox }, Tipo = tipo, Datos = datos });
                Retenidos++;
                if (_hilo == null) { _hilo = new Thread(Repartir) { IsBackground = true, Name = "red-simulada" }; _hilo.Start(); }
            }
            return false;
        }

        private static void Repartir()
        {
            while (true)
            {
                var ahora = DateTime.UtcNow;
                List<Pendiente> listos = null;
                lock (_cola)
                {
                    for (int i = 0; i < _cola.Count; i++)
                        if (_cola[i].Cuando <= ahora) { if (listos == null) listos = new List<Pendiente>(); listos.Add(_cola[i]); }
                    if (listos != null) foreach (var p in listos) _cola.Remove(p);
                }
                if (listos != null)
                {
                    listos.Sort((a, b) => a.Cuando.CompareTo(b.Cuando));
                    foreach (var p in listos) p.Destino.Q.Enqueue((p.Tipo, p.Datos));
                }
                Thread.Sleep(5);
            }
        }
    }
}
