using BrainsCV.MotionImageryStandardsBoard;
using BrainsFFPlayer.FFmpeg;
using BrainsFFPlayer.FFmpeg.GenericOptions;
using BrainsFFPlayer.Utility;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FFmpeg.AutoGen;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Drawing.Imaging;
using System.IO;
using System.Diagnostics;
using PixelFormat = System.Drawing.Imaging.PixelFormat;

namespace BrainsFFPlayer
{
    internal class MainWindowViewModel : ObservableObject
    {
        private readonly FFmpegManager ffmpegManager = new();
        private readonly object videoLock = new();

        private readonly Stopwatch stopwatch = new();
        private long startTime = 0;

        #region Binding Fields

        private string videoUrl = "http://commondatastorage.googleapis.com/gtv-videos-bucket/sample/BigBuckBunny.mp4";
        private BitmapImage? videoFrame;
        private int selectedInputFormatIndex;

        public string VideoUrl { get { return videoUrl; } set { SetProperty(ref videoUrl, value); } }
        public BitmapImage? VideoFrame { get { return videoFrame; } set { SetProperty(ref videoFrame, value); } }
        public int SelectedInputFormatIndex { get { return selectedInputFormatIndex; } set { SetProperty(ref selectedInputFormatIndex, value); } }

        public ObservableCollection<ComboBoxItem> InputFormatCollection { get; set; } = [];

        #endregion

       
        public MainWindowViewModel()
        {
            InitializeAVFormatOptionCollection();
            InitializeCommand();
        }

        private void InitializeAVFormatOptionCollection()
        {
            int i = 0;

            AppData.UIDispatcher.Invoke(() =>
            {
                InputFormatCollection.Clear();

                foreach (var mode in Enum.GetValues(typeof(AVFormatOption)))
                {
                    var option = (AVFormatOption)i;
                    InputFormatCollection.Add(new ComboBoxItem() { Content = option });

                    i++;
                }

                SelectedInputFormatIndex = 0;
            });
        }

        private void InitializeCommand()
        {
            PlayVideo_Command = new RelayCommand(PlayVideo);
            StopVideo_Command = new RelayCommand(StopVideo);
            RecordVideo_Command = new RelayCommand(RecordVideo);
        }

        #region Commands

        public ICommand? PlayVideo_Command { get; set; }
        public ICommand? StopVideo_Command { get; set; }
        public ICommand? RecordVideo_Command { get; set; }

        private void PlayVideo()
        {
            ffmpegManager.PlayVideo(VideoUrl, (AVFormatOption)SelectedInputFormatIndex);
            ffmpegManager.VideoFrameReceived += VideoFrameReceived;
        }

        private void StopVideo()
        {
            stopwatch.Stop();

            ffmpegManager.VideoFrameReceived -= VideoFrameReceived;
            ffmpegManager.DisposeFFmpeg();
        }

        private void RecordVideo()
        {

        }

        #endregion

        private unsafe void VideoFrameReceived(AVFrame frame, MisbMetadata metadata)
        {
            //첫 프레임 받을 때 시작 시간 설정 (for PTS 동기화)
            if (!stopwatch.IsRunning)
            {
                stopwatch.Start();
                startTime = stopwatch.ElapsedMilliseconds;
            }

            Bitmap newFrame = new(frame.width, frame.height, frame.linesize[0], PixelFormat.Format24bppRgb, (IntPtr)frame.data[0]);
            BitmapToImageSource(newFrame);
            SyncAVFrameTimeBase(frame);
        }

        private void BitmapToImageSource(Bitmap frame)
        {
            BitmapImage bitmapImage = new();
            using MemoryStream memoryStream = new();

            lock (videoLock)
            {
                try
                {
                    frame.Save(memoryStream, ImageFormat.Bmp);
                    memoryStream.Position = 0;

                    bitmapImage.BeginInit();
                    bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                    bitmapImage.StreamSource = memoryStream;
                    bitmapImage.EndInit();
                    bitmapImage.Freeze();

                    AppData.UIDispatcher.Invoke(() =>
                    {
                        VideoFrame = bitmapImage;
                    });

                }
                catch (NullReferenceException ex) { Debug.WriteLine("{0}: {1}",ex.Message, ex.StackTrace); }
                catch (TaskCanceledException ex) { Debug.WriteLine("{0}: {1}", ex.Message, ex.StackTrace); }
                finally
                {
                    memoryStream.Dispose();
                }
            }
        }

        /// <summary> 재생 속도 PTS 기반 동적 동기화 </summary>
        private void SyncAVFrameTimeBase(AVFrame frame)
        {
            long pts = frame.pts == ffmpeg.AV_NOPTS_VALUE ? 0 : frame.pts;
            double timeBase = ffmpeg.av_q2d(frame.time_base);
            double frameTimeInSeconds = pts * timeBase;
            double elapsedTime = (stopwatch.ElapsedMilliseconds - startTime) / 1000.0;
            double delay = frameTimeInSeconds - elapsedTime;

            if (delay > 0)
            {
                using ManualResetEventSlim resetEvent = new(false);
                resetEvent.Wait((int)(delay * 1000));
            }
        }

    }
}
