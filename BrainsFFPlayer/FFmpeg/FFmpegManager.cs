using BrainsCV.MotionImageryStandardsBoard;
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
        public event Action<AVFrame, MisbMetadata> VideoFrameReceived = delegate { };

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
            try
            {
                bool hwDecoder = Convert.ToBoolean(state);
                ConfigureHWDecoder(hwDecoder, out hwDeviceType);

                using var decoder = new VideoStreamDecoder(url, avInputOption, avFormatOption,hwDeviceType);

                var info = decoder.GetContextInfo();
                info.ToList().ForEach(x => Debug.WriteLine($"{x.Key} = {x.Value}"));

                var sourceSize = decoder.FrameSize;
                var sourcePixelFormat = hwDeviceType == AVHWDeviceType.AV_HWDEVICE_TYPE_NONE ? decoder.PixelFormat : GetHWPixelFormat(hwDeviceType);
                var destinationSize = sourceSize;
                var destinationPixelFormat = AVPixelFormat.AV_PIX_FMT_BGR24;

                using var vfc = new VideoFrameConverter(sourceSize, sourcePixelFormat, destinationSize, destinationPixelFormat);

                while (decoder.TryDecodeNextFrame(out var vmti, out var avFrame) && isDecodingEvent!.WaitOne())
                {
                    videoInfo = decoder.GetVideoInfo();
                    var convertedFrame = vfc.Convert(avFrame);

                    if (isRecord)
                    {
                        decodedFrameQueue.Enqueue(convertedFrame);
                    }

                    VideoInfoReceived?.Invoke(videoInfo);
                    VideoFrameReceived?.Invoke(convertedFrame, vmti);
                }
            }
            catch (ApplicationException e)
            {
                Debug.WriteLine(e.Message);
            }
            catch (ObjectDisposedException e)
            {
                Debug.WriteLine(e.Message);
            }
            catch (AccessViolationException e)
            {
                Debug.WriteLine(e.Message);
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

                        h264Encoder?.TryEncodeNextPacket(convertedFrame, videoInfo);
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
