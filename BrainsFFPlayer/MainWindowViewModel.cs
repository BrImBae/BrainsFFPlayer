using BrainsCV.MotionImageryStandardsBoard;
using BrainsFFPlayer.FFmpeg;
using BrainsFFPlayer.FFmpeg.GenericOptions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FFmpeg.AutoGen;
using System.Drawing;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Drawing.Imaging;
using System.IO;
using System.Diagnostics;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using BrainsFFPlayer.Utility;
using PixelFormat = System.Drawing.Imaging.PixelFormat;

namespace BrainsFFPlayer
{
    internal class MainWindowViewModel : ObservableObject
    {
        private readonly FFmpegManager ffmpegManager = new();
        private readonly object videoLock = new();

        private readonly Stopwatch stopwatch = new();
        private long startTime = 0;

        private const long usScale = 1000000;

        private bool isRecord;

        #region Binding Fields

        private bool isPlay;
        private string recordText = "녹화";
        private string videoUrl = "http://commondatastorage.googleapis.com/gtv-videos-bucket/sample/BigBuckBunny.mp4";
        private BitmapImage? videoFrame;
        private int selectedInputFormatIndex;
        private string totalPlayTime = "00:00:00";
        private string playTime = "00:00:00";

        public bool IsPlay { get { return isPlay; } set { SetProperty(ref isPlay, value); } }
        public string RecordText { get { return recordText; } set { SetProperty(ref recordText, value); } }
        public string VideoUrl { get { return videoUrl; } set { SetProperty(ref videoUrl, value); } }
        public BitmapImage? VideoFrame { get { return videoFrame; } set { SetProperty(ref videoFrame, value); } }
        public int SelectedInputFormatIndex { get { return selectedInputFormatIndex; } set { SetProperty(ref selectedInputFormatIndex, value); } }
        public string TotalPlayTime { get { return totalPlayTime; } set { SetProperty(ref totalPlayTime, value); } }
        public string PlayTime { get { return playTime; } set { SetProperty(ref playTime, value); } }

        #region Video Info

        private long totalDuration;
        private long duration;
        private string frameSize = "-";
        private string frameRate = "-";
        private long probeSize;
        private long averageBitrate;
        private long bitrate;
        private int gopSize;
        private string codecID = "-";
        private string pixcelFormat = "-";
        private int profile;
        private int level;
        private int qMin;
        private int qMax;
        private int maxBFrames;
        private string sampleAspectRatio = "-";
        private string timebase = "-";
        private int threadCount;
        private int rcBufferSize;
        private long rcMaxRate;
        private int delay;

        public long TotalDuration { get { return totalDuration; } set { SetProperty(ref totalDuration, value); } }
        public long Duration { get { return duration; } set { SetProperty(ref duration, value); } }
        public string FrameSize { get { return frameSize; } set { SetProperty(ref frameSize, value); } }
        public string FrameRate { get { return frameRate; } set { SetProperty(ref frameRate, value); } }
        public long ProbeSize { get { return probeSize; } set { SetProperty(ref probeSize, value); } }
        public long AverageBitrate { get { return averageBitrate; } set { SetProperty(ref averageBitrate, value); } }
        public long Bitrate { get { return bitrate; } set { SetProperty(ref bitrate, value); } }
        public int GopSize { get { return gopSize; } set { SetProperty(ref gopSize, value); } }
        public string CodecID { get { return codecID; } set { SetProperty(ref codecID, value); } }
        public string PixcelFormat { get { return pixcelFormat; } set { SetProperty(ref pixcelFormat, value); } }
        public int Profile { get { return profile; } set { SetProperty(ref profile, value); } }
        public int Level { get { return level; } set { SetProperty(ref level, value); } }
        public int QMin { get { return qMin; } set { SetProperty(ref qMin, value); } }
        public int QMax { get { return qMax; } set { SetProperty(ref qMax, value); } }
        public int MaxBFrames { get { return maxBFrames; } set { SetProperty(ref maxBFrames, value); } }
        public string SampleAspectRatio { get { return sampleAspectRatio; } set { SetProperty(ref sampleAspectRatio, value); } }
        public string Timebase { get { return timebase; } set { SetProperty(ref timebase, value); } }
        public int ThreadCount { get { return threadCount; } set { SetProperty(ref threadCount, value); } }
        public int RcBufferSize { get { return rcBufferSize; } set { SetProperty(ref rcBufferSize, value); } }
        public long RcMaxRate { get { return rcMaxRate; } set { SetProperty(ref rcMaxRate, value); } }
        public int Delay { get { return delay; } set { SetProperty(ref delay, value); } }

        #endregion

        #region Video Option

        private bool isHwDecoderDXVA2;

        public bool IsHwDecoderDXVA2 { get { return isHwDecoderDXVA2; } set { SetProperty(ref isHwDecoderDXVA2, value); } }
        public ObservableCollection<AVFormatOptionItem> FormatOptionItems { get; set; } = [];

        #endregion

        #endregion

        public MainWindowViewModel()
        {
            InitializeCommand();
            InitializeAVFormatOptionCollection();
        }

        private void InitializeCommand()
        {
            OpenVideoFile_Command = new RelayCommand(OpenVideoFile);
            PlayVideo_Command = new RelayCommand(PlayVideo);
            StopVideo_Command = new RelayCommand(StopVideo);
            RecordVideo_Command = new RelayCommand(RecordVideo);
        }

        private void InitializeAVFormatOptionCollection()
        {
            foreach (AVFormatOption options in Enum.GetValues(typeof(AVFormatOption)))
            {
                var option = EnumHelper.GetAVFormatOption(options);

                if (option != null)
                {
                    FormatOptionItems.Add(new AVFormatOptionItem { Key = option.Key, Value = option.Value, Desciption = option.Description });
                }
            }
        }

        #region Commands

        public ICommand? OpenVideoFile_Command { get; set; }
        public ICommand? PlayVideo_Command { get; set; }
        public ICommand? StopVideo_Command { get; set; }
        public ICommand? RecordVideo_Command { get; set; }
        
        private void OpenVideoFile()
        {
            OpenFileDialog openFileDialog = new OpenFileDialog
            {
                Filter = "MP4 (*.mp4)|*.mp4|MPEG-TS (*.ts)|*.ts"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                VideoUrl = openFileDialog.FileName;
            }
        }

        private void PlayVideo()
        {
            ffmpegManager.PlayVideo(VideoUrl, (AVInputOption)SelectedInputFormatIndex, GetFormatOption(), IsHwDecoderDXVA2);
         
            ffmpegManager.VideoInfoReceived += VideoInfoReceived;
            ffmpegManager.VideoFrameReceived += VideoFrameReceived;

            IsPlay = true;
        }

        private void StopVideo()
        {
            stopwatch.Stop();

            ffmpegManager.VideoInfoReceived -= VideoInfoReceived;
            ffmpegManager.VideoFrameReceived -= VideoFrameReceived;

            ffmpegManager.DisposeFFmpeg();

            IsPlay = false;
        }

        private void RecordVideo()
        {
            isRecord = !isRecord;

            if(isRecord)
            {
                RecordText = "녹화 중지";
            }
            else
            {
                RecordText = "녹화";
            }
        }

        #endregion

        private List<Tuple<string, string>> GetFormatOption()
        {
            List<Tuple<string, string>> formatTuple = [];

            foreach (var item in FormatOptionItems)
            {
                if (item.IsChecked)
                {
                    formatTuple.Add(Tuple.Create(item.Key, item.Value));
                }
            }

            return formatTuple;
        }

        private void VideoInfoReceived(VideoInfo info)
        {
            TotalDuration = info.TotalDuration / usScale;
            FrameSize = string.Format("{0}x{1}", info.FrameSize.Width, info.FrameSize.Height);
            FrameRate = info.FrameRate.den > 0 ? string.Format("{0}", info.FrameRate.num / info.FrameRate.den) : "알 수 없음";
            ProbeSize = info.ProbeSize;
            AverageBitrate = info.AverageBitrate;
            Bitrate = info.Bitrate;
            GopSize = info.GopSize;
            CodecID = info.CodecID.ToString();
            PixcelFormat = info.PixcelFormat.ToString();
            Profile = info.Profile;
            Level = info.Level;
            QMin = info.QMin;
            QMax = info.QMax;
            MaxBFrames = info.MaxBFrames;
            SampleAspectRatio = info.SampleAspectRatio.den > 0 ? string.Format("{0}", info.SampleAspectRatio.num / info.SampleAspectRatio.den) : "알 수 없음";
            Timebase = info.Timebase.den > 0 ? string.Format("{0}", info.Timebase.num / info.Timebase.den) : "알 수 없음";
            ThreadCount = info.ThreadCount;
            RcBufferSize = info.RcBufferSize;
            RcMaxRate = info.RcMaxRate;
            Delay = info.Delay;

            TotalPlayTime = ConvertDuration(TotalDuration);
        }

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

            PlayTime = ConvertDuration((long)frameTimeInSeconds);
            Duration = (long)frameTimeInSeconds;

            if (delay > 0)
            {
                using ManualResetEventSlim resetEvent = new(false);
                resetEvent.Wait((int)(delay * 1000));
            }
        }

        private string ConvertDuration(long duration)
        {
            if (duration < 0) return "00:00:00";

            var sec = duration % 60;
            int min = (int)(duration / 60) % 60;
            int hour = (int)(duration / 3600) % 24;

            return string.Format("{0}:{1}:{2}", hour.ToString("00"), min.ToString("00"), sec.ToString("00"));
        }
    }
}
