using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;

namespace BrainsFFPlayer
{
    internal class AppData
    {
        public static readonly Dispatcher UIDispatcher = Application.Current.Dispatcher;

        public string VideoUrl = "";
        public int InputFormatIndex = -1;
        public bool IsHwDecoderDXVA2;
        public ObservableCollection<AVFormatOptionItem> FormatOptionItems { get; set; } = [];

        #region SingleTon

        private static readonly Lazy<AppData> lazy = new Lazy<AppData>(() => new AppData());

        public static AppData Instance { get { return lazy.Value; } }

        private AppData() 
        {
            //---test video url---//
            //rtsp://210.99.70.120:1935/live/cctv001.stream
            //http://commondatastorage.googleapis.com/gtv-videos-bucket/sample/BigBuckBunny.mp4
        }

        #endregion
    }
}
