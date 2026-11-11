namespace MoonlighterCoop
{
    /// Una opción del propio juego del móvil (en el port de Android, «Rendimiento: auto / batería / calidad»)
    /// que sale en el menú del cooperativo, porque en el móvil es el menú que está a mano. En el PC no hay
    /// ninguna: las dos quedan vacías y el menú no la enseña.
    public static class OpcionMovil
    {
        public static System.Func<string> Texto;
        public static System.Action Siguiente;
        /// Abre el editor para colocar los botones táctiles a gusto (null en el PC)
        public static System.Action MoverBotones;
    }
}
