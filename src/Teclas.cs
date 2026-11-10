using System.Collections.Generic;
using UnityEngine;

namespace MoonlighterCoop
{
    /// Teclas del mod que también funcionan en el móvil (port de Android).
    /// En el PC es lo mismo que Input.GetKeyDown. En el móvil no hay teclado: los botones táctiles
    /// "pulsan" la tecla con Pulsar(), y los avisos que esperan una tecla la ofrecen con Ofrecer()
    /// para que el móvil enseñe un botón con su texto mientras el aviso está a la vista.
    public static class Teclas
    {
        public static bool Movil => Application.isMobilePlatform;

        private static readonly Dictionary<KeyCode, int> _pulsadas = new Dictionary<KeyCode, int>();

        public struct Opcion
        {
            public KeyCode Tecla;
            /// Si no está vacío, es un botón del mando ("A", "B"...) en vez de una tecla: para menús del propio juego
            public string Boton;
            public string Texto;
        }

        private static readonly List<Opcion> _opciones = new List<Opcion>();
        private static int _framesOpciones = -10;

        /// Como Input.GetKeyDown, pero también vale el botón táctil que "pulsó" esa tecla.
        public static bool Abajo(KeyCode k)
        {
            if (Input.GetKeyDown(k)) return true;
            int f;
            return _pulsadas.TryGetValue(k, out f) && f == Time.frameCount;
        }

        /// Lo llaman los botones táctiles: la tecla cuenta como pulsada en el siguiente fotograma.
        public static void Pulsar(KeyCode k)
        {
            _pulsadas[k] = Time.frameCount + 1;
        }

        /// Un aviso que espera una tecla la ofrece cada fotograma mientras se ve.
        public static void Ofrecer(KeyCode k, string texto)
        {
            if (_framesOpciones != Time.frameCount)
            {
                _opciones.Clear();
                _framesOpciones = Time.frameCount;
            }
            for (int i = 0; i < _opciones.Count; i++)
                if (_opciones[i].Tecla == k) return;
            _opciones.Add(new Opcion { Tecla = k, Texto = texto });
        }

        /// Como Ofrecer, pero con un botón del mando ("A", "B"...): el móvil lo pulsa en el mando táctil.
        public static void OfrecerBoton(string boton, string texto)
        {
            if (_framesOpciones != Time.frameCount)
            {
                _opciones.Clear();
                _framesOpciones = Time.frameCount;
            }
            for (int i = 0; i < _opciones.Count; i++)
                if (_opciones[i].Boton == boton) return;
            _opciones.Add(new Opcion { Tecla = KeyCode.None, Boton = boton, Texto = texto });
        }

        /// Teclas que piden los avisos ahora mismo (vacía si no hay ninguno a la vista).
        public static List<Opcion> Ofrecidas()
        {
            if (Time.frameCount - _framesOpciones > 1) _opciones.Clear();
            return _opciones;
        }

        /// Texto según dónde se juega: en el PC la tecla, en el móvil el botón táctil.
        public static string Nombre(string pc, string movil) => Movil ? movil : pc;
    }
}
