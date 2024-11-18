using System.Windows;
using System.Windows.Threading;

namespace BrainsFFPlayer
{
    internal class AppData
    {
        public static readonly Dispatcher UIDispatcher = Application.Current.Dispatcher;

        #region SingleTon

        private static readonly Lazy<AppData> lazy = new Lazy<AppData>(() => new AppData());

        public static AppData Instance { get { return lazy.Value; } }

        private AppData() { }

        #endregion
    }
}
