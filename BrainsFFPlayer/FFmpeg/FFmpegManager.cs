using BrainsCV;

using BrainsFFPlayer.FFmpeg.Core;
using BrainsFFPlayer.FFmpeg.GenericOptions;
using FFmpeg.AutoGen;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace BrainsFFPlayer.FFmpeg
{
    public unsafe class FFmpegManager
    {
        private VideoInfo videoInfo = new();

        private readonly ConcurrentQueue<AVFrame> decodedFrameQueue = new();
        private AVHWDeviceType hwDeviceType;

        private AVFrame queueFrame;
        private AVInputOption avInputOption;
        private List<Tuple<string, string>> avFormatOption = [];

        private H264VideoStreamEncoder? h264Encoder;
        private ManualResetEvent? isEncodingEvent;
        private ManualResetEvent? isDecodingEvent;

        private string url = string.Empty;

        private bool isRecord;
        private bool isRecordComplete;
        private bool isDecodingThreadRunning;

        public event Action<VideoInfo> VideoInfoReceived = delegate { };
        public event Action<VideoFrameWithMISB> VideoFrameReceived = delegate { };
        public event Action InvalidVideoExit = delegate { };

        public FFmpegManager()
        {
            try
            {
                FFmpegBinariesHelper.RegisterFFmpegBinaries();
            }
            catch (NotSupportedException ex) { Debug.WriteLine(ex.Message); }
        }

        public void InitializeFFmpeg(string _url, AVInputOption _avInputOption)
        {
            url = _url;
            avInputOption = _avInputOption;
        }

        public void PlayVideo(string _url, AVInputOption _avInputOption, List<Tuple<string, string>> _avFormatOption, bool isHwDecoderDXVA2 = false)
        {
            url = _url;
            avInputOption = _avInputOption;
            avFormatOption = _avFormatOption;

            ThreadPool.QueueUserWorkItem(new WaitCallback(DecodeAllFramesToImages), isHwDecoderDXVA2);
            isDecodingEvent = new(false);
            isDecodingEvent.Set();

            isDecodingThreadRunning = true;
        }

        public void StopVideo()
        {
            if (isDecodingThreadRunning)
            {
                isDecodingThreadRunning = false;

                isDecodingEvent?.Reset();
                isDecodingEvent?.Dispose();
            }
        }

        public void RecordVideo(string fileName)
        {
            isEncodingEvent = new ManualResetEvent(false);      //녹화 시마다 초기화 필요
            h264Encoder = new H264VideoStreamEncoder();

            //initialize output format&codec
            h264Encoder.OpenOutputURL(fileName, videoInfo);

            ThreadPool.QueueUserWorkItem(new WaitCallback(EncodeImagesToH264));

            isEncodingEvent.Set();

            isRecord = true;
            isRecordComplete = false;
        }

        public int StopRecord()
        {
            try
            {
                isRecord = false;
                isEncodingEvent?.Reset();

                h264Encoder?.FlushEncode();
                h264Encoder?.Dispose();

                isRecordComplete = true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.ToString());
                return -1;
            }

            return 0;
        }

        private void ConfigureHWDecoder(bool useHwAcc, out AVHWDeviceType HWtype)
        {
            HWtype = AVHWDeviceType.AV_HWDEVICE_TYPE_NONE;

            if (useHwAcc)
            {
                var availableHWDecoders = new Dictionary<int, AVHWDeviceType>();

                Debug.WriteLine("Select hardware decoder:");
                var type = AVHWDeviceType.AV_HWDEVICE_TYPE_NONE;
                var number = 0;

                while ((type = ffmpeg.av_hwdevice_iterate_types(type)) != AVHWDeviceType.AV_HWDEVICE_TYPE_NONE)
                {
                    Debug.WriteLine($"{++number}. {type}");
                    availableHWDecoders.Add(number, type);
                }
                if (availableHWDecoders.Count == 0)
                {
                    Debug.WriteLine("Your system have no hardware decoders.");
                    HWtype = AVHWDeviceType.AV_HWDEVICE_TYPE_NONE;
                    return;
                }

                int decoderNumber = availableHWDecoders.SingleOrDefault(t => t.Value == AVHWDeviceType.AV_HWDEVICE_TYPE_DXVA2).Key;

                if (decoderNumber == 0)
                {
                    decoderNumber = availableHWDecoders.First().Key;
                }

                Debug.WriteLine($"Selected [{decoderNumber}]");

                int.TryParse(Console.ReadLine(), out var inputDecoderNumber);

                availableHWDecoders.TryGetValue(inputDecoderNumber == 0 ? decoderNumber : inputDecoderNumber, out HWtype);
            }
        }

        private void DecodeAllFramesToImages(object? state)
        {
            // 임시 오류 유예창(상위): 네트워크 hiccup을 일정 시간 허용
            var tempErrTimer = new Stopwatch();
            bool tempErrActive = false;
            // 환경에 맞게 조정 (무선/위성 등은 더 길게)
            TimeSpan maxTempErrDuration = TimeSpan.FromSeconds(5);
            const int TEMP_SLEEP_MS = 10;

            try
            {
                ConfigureHWDecoder(false, out hwDeviceType);

                using var decoder = new VideoStreamDecoder(url, avInputOption, avFormatOption, hwDeviceType);
                videoInfo = decoder.GetVideoInfo();
                VideoInfoReceived?.Invoke(videoInfo);

                using var vfc = new VideoFrameConverter(
                    decoder.FrameSize,
                    hwDeviceType == AVHWDeviceType.AV_HWDEVICE_TYPE_NONE ? decoder.PixelFormat : GetHWPixelFormat(hwDeviceType),
                    decoder.FrameSize,
                    AVPixelFormat.AV_PIX_FMT_BGR24);

                while (isDecodingThreadRunning)
                {
                    var status = decoder.TryDecodeNextFrame(out var vmti, out var avFrame);                    

                    switch (status)
                    {
                        case DecodeStatus.Success:
                            {
                                if (tempErrActive)
                                {
                                    tempErrActive = false; tempErrTimer.Reset();
                                }

                                if (!isDecodingEvent!.WaitOne())
                                {
                                    break;
                                }

                                var converted = vfc.Convert(avFrame);

                                if (isRecord)
                                {
                                    decodedFrameQueue.Enqueue(converted);
                                }

                                try
                                {
                                    VideoFrameReceived?.Invoke(new VideoFrameWithMISB { MISB = vmti, VideoFrame = converted });
                                }
                                catch (Exception) { }

                                break;
                            }

                        case DecodeStatus.TryAgain:
                            {
                                if (!tempErrActive)
                                {
                                    tempErrActive = true;
                                    tempErrTimer.Restart();
                                }

                                if (tempErrTimer.Elapsed < maxTempErrDuration)
                                {
                                    // 바쁜 루프 방지
                                    Thread.Sleep(TEMP_SLEEP_MS);
                                    continue;
                                }

                                InvalidVideoExit?.Invoke();
                                isDecodingThreadRunning = false;
                                break;
                            }

                        case DecodeStatus.StreamEnded:
                        default:
                            {
                                InvalidVideoExit?.Invoke();
                                isDecodingThreadRunning = false;
                                break;
                            }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);
                InvalidVideoExit?.Invoke();
            }
        }

        private unsafe void EncodeImagesToH264(object? state)
        {
            try
            {
                while (isEncodingEvent!.WaitOne())
                {
                    if (decodedFrameQueue.TryDequeue(out queueFrame))
                    {
                        var sourcePixelFormat = AVPixelFormat.AV_PIX_FMT_BGR24;
                        var destinationPixelFormat = AVPixelFormat.AV_PIX_FMT_YUV420P; //for h.264

                        using var vfc = new VideoFrameConverter(videoInfo.FrameSize, sourcePixelFormat, videoInfo.FrameSize, destinationPixelFormat);
                        var convertedFrame = vfc.Convert(queueFrame);

                        h264Encoder?.TryEncodeNextPacket(convertedFrame);
                    }
                }
            }
            catch (ObjectDisposedException e)
            {
                Console.WriteLine(e.Message);
            }
            catch (AccessViolationException e)
            {
                Console.WriteLine(e.Message);
            }
        }

        private AVPixelFormat GetHWPixelFormat(AVHWDeviceType hWDevice)
        {
            switch (hWDevice)
            {
                case AVHWDeviceType.AV_HWDEVICE_TYPE_NONE:
                    return AVPixelFormat.AV_PIX_FMT_NONE;
                case AVHWDeviceType.AV_HWDEVICE_TYPE_VDPAU:
                    return AVPixelFormat.AV_PIX_FMT_VDPAU;
                case AVHWDeviceType.AV_HWDEVICE_TYPE_CUDA:
                    return AVPixelFormat.AV_PIX_FMT_CUDA;
                case AVHWDeviceType.AV_HWDEVICE_TYPE_VAAPI:
                    return AVPixelFormat.AV_PIX_FMT_VAAPI;
                case AVHWDeviceType.AV_HWDEVICE_TYPE_DXVA2:
                    return AVPixelFormat.AV_PIX_FMT_NV12;
                case AVHWDeviceType.AV_HWDEVICE_TYPE_QSV:
                    return AVPixelFormat.AV_PIX_FMT_QSV;
                case AVHWDeviceType.AV_HWDEVICE_TYPE_VIDEOTOOLBOX:
                    return AVPixelFormat.AV_PIX_FMT_VIDEOTOOLBOX;
                case AVHWDeviceType.AV_HWDEVICE_TYPE_D3D11VA:
                    return AVPixelFormat.AV_PIX_FMT_NV12;
                case AVHWDeviceType.AV_HWDEVICE_TYPE_DRM:
                    return AVPixelFormat.AV_PIX_FMT_DRM_PRIME;
                case AVHWDeviceType.AV_HWDEVICE_TYPE_OPENCL:
                    return AVPixelFormat.AV_PIX_FMT_OPENCL;
                case AVHWDeviceType.AV_HWDEVICE_TYPE_MEDIACODEC:
                    return AVPixelFormat.AV_PIX_FMT_MEDIACODEC;
                default:
                    return AVPixelFormat.AV_PIX_FMT_NONE;
            }
        }

        public void DisposeFFmpeg()
        {
            if (isDecodingThreadRunning)
            {
                isDecodingThreadRunning = false;

                isDecodingEvent?.Reset();
                isDecodingEvent?.Dispose();
            }

            if (isRecord)
            {
                isRecord = false;

                isEncodingEvent?.Reset();
                isEncodingEvent?.Dispose();

                if (!isRecordComplete)
                {
                    h264Encoder?.FlushEncode();
                    h264Encoder?.Dispose();
                }
            }
        }
    }
}
