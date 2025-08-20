using BrainsCV.MotionImageryStandardsBoard;
using BrainsFFPlayer.FFmpeg.GenericOptions;
using BrainsFFPlayer.Utility;
using FFmpeg.AutoGen;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;

namespace BrainsFFPlayer.FFmpeg.Core
{
    internal unsafe class VideoStreamDecoder : IDisposable
    {
        private readonly AVCodecContext* pCodecContext;
        private readonly AVFormatContext* pFormatContext;

        private readonly AVPacket* pPacket;
        private readonly AVFrame* pFrame;
        private readonly AVFrame* receivedFrame;

        private readonly int videoIndex;
        private readonly int dataIndex;

        public Size FrameSize { get; }
        public AVPixelFormat PixelFormat { get; }

        public VideoStreamDecoder(string url, AVInputOption avInputOption, List<Tuple<string, string>> avFormatOption, AVHWDeviceType HWDeviceType = AVHWDeviceType.AV_HWDEVICE_TYPE_NONE)
        {
            try
            {
                ffmpeg.avdevice_register_all();
                ffmpeg.avformat_network_init();

                pFormatContext = ffmpeg.avformat_alloc_context();
                receivedFrame = ffmpeg.av_frame_alloc();
                var pFormat = pFormatContext;

                AVInputFormat* iformat = null;

                var inputFormat = EnumHelper.GetAVFormatOption(avInputOption);

                if (inputFormat != null)
                {
                    iformat = ffmpeg.av_find_input_format(inputFormat.Key);
                }

                AVDictionary* options;

                foreach(var option in avFormatOption)
                {
                    ffmpeg.av_dict_set(&options, option.Item1, option.Item2, 0);
                }

                switch (avInputOption)
                {
                    case AVInputOption.AUTO:
                        ffmpeg.avformat_open_input(&pFormat, url, null, &options).ThrowExceptionIfError();
                        break;
                    case AVInputOption.MP4:
                    case AVInputOption.MPEG_TS:
                    case AVInputOption.DSHOW:
                        //TODO
                    case AVInputOption.RTP:
                    case AVInputOption.RTSP:
                        //TODO
                        ffmpeg.avformat_open_input(&pFormat, url, iformat, &options).ThrowExceptionIfError();
                        break;
                }

                ffmpeg.av_dict_free(&options);

                ffmpeg.avformat_find_stream_info(pFormatContext, null).ThrowExceptionIfError();

                AVCodec* codec = null;

                videoIndex = ffmpeg.av_find_best_stream(pFormatContext, AVMediaType.AVMEDIA_TYPE_VIDEO, -1, -1, &codec, 0).ThrowExceptionIfError();
                dataIndex = ffmpeg.av_find_best_stream(pFormatContext, AVMediaType.AVMEDIA_TYPE_DATA, -1, -1, null, 0);

                pCodecContext = ffmpeg.avcodec_alloc_context3(codec);

                //실시간 영상 스트리밍시 아래 활성화시 재생 안됨 문제로 임시 비활성
                //ffmpeg.av_opt_set(pCodecContext->priv_data, "preset", "fast", 0);
                //ffmpeg.av_opt_set(pCodecContext->priv_data, "tune", "zerolatency", 0);

                if (HWDeviceType != AVHWDeviceType.AV_HWDEVICE_TYPE_NONE)
                {
                    ffmpeg.av_hwdevice_ctx_create(&pCodecContext->hw_device_ctx, HWDeviceType, null, null, 0).ThrowExceptionIfError();
                }

                ffmpeg.avcodec_parameters_to_context(pCodecContext, pFormatContext->streams[videoIndex]->codecpar).ThrowExceptionIfError();
                ffmpeg.avcodec_open2(pCodecContext, codec, null).ThrowExceptionIfError();

                FrameSize = new Size(pCodecContext->width, pCodecContext->height);
                PixelFormat = pCodecContext->pix_fmt;

                pPacket = ffmpeg.av_packet_alloc();
                pFrame = ffmpeg.av_frame_alloc();
            }
            catch (AccessViolationException ex)
            {
                throw new AccessViolationException("Access Violation Exception", ex);
            }
        }

        public DecodeStatus TryDecodeNextFrame(out MisbMetadata vmti, out AVFrame frame)
        {
            vmti = new();
            frame = default;

            ffmpeg.av_frame_unref(pFrame);
            ffmpeg.av_frame_unref(receivedFrame);

            AVRational time_base = pFormatContext->streams[videoIndex]->time_base;

            // 하위(프레임 수신) 유예창: 디코더가 준비될 때까지 잠깐 기다림
            var retryWindow = new Stopwatch();
            retryWindow.Start();

            TimeSpan maxRetryDuration = TimeSpan.FromSeconds(3);
            const int TEMP_SLEEP_MS = 10;

            while (true)
            {
                int error;

                try
                {
                    // ---------------------------
                    // 1) 패킷 읽기 (read_frame loop)
                    // ---------------------------
                    while (true)
                    {
                        ffmpeg.av_packet_unref(pPacket);
                        error = ffmpeg.av_read_frame(pFormatContext, pPacket);

                        if (error == ffmpeg.AVERROR_EOF)
                        {
                            // 실제 입력 종료
                            return DecodeStatus.StreamEnded;
                        }

                        // 네트워크 지연/일시 오류: EAGAIN, I/O(-5) → 유예창 내 재시도
                        if (error == ffmpeg.AVERROR(ffmpeg.EAGAIN) || error == -5)
                        {
                            if (retryWindow.Elapsed < maxRetryDuration)
                            {
                                Thread.Sleep(TEMP_SLEEP_MS);
                                continue;
                            }
                            return DecodeStatus.TryAgain;
                        }

                        // 그 외 오류는 치명적 → 상위에서 종료하도록 StreamEnded 처리
                        error.ThrowExceptionIfError();

                        // ---- MISB(DATA) 처리: 예외 무해화 ----
                        if (dataIndex >= 0 &&
                            pPacket->stream_index == dataIndex &&
                            pFormatContext->streams[dataIndex]->codecpar->codec_type == AVMediaType.AVMEDIA_TYPE_DATA &&
                            MISB.IsMISBMetadata(pPacket->size, pPacket->data))
                        {
                            try
                            {
                                vmti = MISB.ProcessPacketMetadata(pPacket->size, pPacket->data);
                            }
                            catch (Exception)
                            {
                                vmti = new MisbMetadata();
                            }

                            // 데이터 패킷이면 다음 패킷 계속
                            continue;
                        }

                        // 비디오 패킷을 만날 때까지 계속 읽기
                        if (pPacket->stream_index != videoIndex)
                            continue;

                        break; // 비디오 패킷 확보
                    }

                    // --------------------------------
                    // 2) 디코더에 비디오 패킷 전송(send)
                    // --------------------------------
                    error = ffmpeg.avcodec_send_packet(pCodecContext, pPacket);

                    if (error == ffmpeg.AVERROR(ffmpeg.EAGAIN))
                    {
                        // 디코더 버퍼가 가득 찼음 → 유예창 내 재시도
                        if (retryWindow.Elapsed < maxRetryDuration)
                        {
                            Thread.Sleep(TEMP_SLEEP_MS);
                            continue;
                        }

                        return DecodeStatus.TryAgain;
                    }

                    if (error == ffmpeg.AVERROR_EOF)
                    {
                        // 디코더가 flush/종료 상태. 실시간 환경에선 일시적일 수 있으므로 TryAgain으로 처리.
                        return DecodeStatus.TryAgain;
                    }

                    error.ThrowExceptionIfError();
                }
                catch (Exception ex)
                {
                    // read/send 중 치유 가능한 오류: 유예창을 상위에 넘겨 재시도
                    Debug.WriteLine($"Error while reading/sending packet: {ex.Message}");
                    return DecodeStatus.TryAgain;
                }
                finally
                {
                    ffmpeg.av_packet_unref(pPacket);
                }

                // -------------------------------
                // 3) 프레임 수신(receive_frame)
                // -------------------------------
                int rcv = ffmpeg.avcodec_receive_frame(pCodecContext, pFrame);

                if (rcv == ffmpeg.AVERROR(ffmpeg.EAGAIN))
                {
                    if (retryWindow.Elapsed < maxRetryDuration)
                    {
                        Thread.Sleep(TEMP_SLEEP_MS);
                        continue;
                    }
                    return DecodeStatus.TryAgain;
                }

                if (rcv == ffmpeg.AVERROR_EOF)
                {
                    // 디코더가 더 이상 출력하지 않음 → 종료
                    return DecodeStatus.StreamEnded;
                }

                rcv.ThrowExceptionIfError();

                // -------------------------------
                // 4) HW → SW 전송(필요 시)
                // -------------------------------
                if (pCodecContext->hw_device_ctx != null)
                {
                    if (ffmpeg.av_hwframe_transfer_data(receivedFrame, pFrame, 0) >= 0)
                        frame = *receivedFrame;
                    else
                        frame = *pFrame; // Fallback (치명적 아님)
                }
                else
                {
                    frame = *pFrame;
                }

                // 타임스탬프 보정
                frame.time_base = time_base;
                frame.duration = pFrame->duration;
                frame.pts = pFrame->pts;

                return DecodeStatus.Success;
            }
        }

        public IReadOnlyDictionary<string, string> GetContextInfo()
        {
            AVDictionaryEntry* tag = null;
            var result = new Dictionary<string, string>();

            while ((tag = ffmpeg.av_dict_get(pFormatContext->metadata, "", tag, ffmpeg.AV_DICT_IGNORE_SUFFIX)) != null)
            {
                var key = Marshal.PtrToStringAnsi((IntPtr)tag->key);
                var value = Marshal.PtrToStringAnsi((IntPtr)tag->value);

                if (key != null && value != null)
                {
                    result.Add(key, value);
                }
            }

            return result;
        }

        public VideoInfo GetVideoInfo()
        {
            VideoInfo videoInfo = new()
            {
                TotalDuration = pFormatContext->duration,
                FrameSize = new Size(pCodecContext->width, pCodecContext->height),
                FrameRate = pFormatContext->streams[videoIndex]->avg_frame_rate,
                ProbeSize = pFormatContext->probesize,
                AverageBitrate = pFormatContext->bit_rate,

                Bitrate = pCodecContext->bit_rate,
                GopSize = pCodecContext->gop_size,
                CodecID = pCodecContext->codec_id,
                PixcelFormat = pCodecContext->pix_fmt,
                Profile = pCodecContext->profile,
                Level = pCodecContext->level,
                QMin = pCodecContext->qmin,
                QMax = pCodecContext->qmax,
                MaxBFrames = pCodecContext->max_b_frames,
                SampleAspectRatio = pCodecContext->sample_aspect_ratio,

                ThreadCount = pCodecContext->thread_count,
                RcBufferSize = pCodecContext->rc_buffer_size,
                RcMaxRate = pCodecContext->rc_max_rate,
                Delay = pCodecContext->delay,
                Timebase = pFormatContext->streams[videoIndex]->time_base
            };

            return videoInfo;
        }

        #region Dispose

        public void Dispose()
        {
            var frame = pFrame;
            ffmpeg.av_frame_free(&frame);

            var packet = pPacket;
            ffmpeg.av_packet_free(&packet);

            var codecContext = pCodecContext;
            ffmpeg.avcodec_free_context(&codecContext);

            var pFormat = pFormatContext;
            ffmpeg.avformat_close_input(&pFormat);
        }

        #endregion
    }
}
