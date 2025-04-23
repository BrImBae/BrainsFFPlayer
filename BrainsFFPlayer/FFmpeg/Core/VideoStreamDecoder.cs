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

        public bool TryDecodeNextFrame(out MisbMetadata vmti, out AVFrame frame)
        {            
            vmti = new();
            frame = *pFrame;

            ffmpeg.av_frame_unref(pFrame);
            ffmpeg.av_frame_unref(receivedFrame);

            int error;
            AVRational time_base = pFormatContext->streams[videoIndex]->time_base;

            while (true)
            {
                try
                {
                    // 새로운 패킷을 가져오기 위해 기존 패킷 해제
                    ffmpeg.av_packet_unref(pPacket);

                    //디버깅으로 장시간 디코딩 멈출 경우 -5 에러 발생 (I/O error)
                    error = ffmpeg.av_read_frame(pFormatContext, pPacket);

                    if (error == ffmpeg.AVERROR_EOF || error == -5)
                    {
                        return false;
                    }

                    if (dataIndex >= 0 && pFormatContext->streams[dataIndex]->codecpar->codec_type == AVMediaType.AVMEDIA_TYPE_DATA)
                    {
                        if (MISB.IsMISBMetadata(pPacket->size, pPacket->data))
                        {
                            vmti = MISB.ProcessPacketMetadata(pPacket->size, pPacket->data);
                        }
                    }

                    // 비디오 스트림 패킷인지 확인
                    if (pPacket->stream_index != videoIndex)
                        continue;

                    // 패킷을 디코더에 전달
                    error = ffmpeg.avcodec_send_packet(pCodecContext, pPacket);

                    if (error == ffmpeg.AVERROR(ffmpeg.EAGAIN))
                    {
                        // 디코더가 꽉 차 있음 → 이전 프레임을 받아야 함
                        continue;
                    }
                    else if (error < 0)
                    {
                        return false;
                    }
                }
                finally
                {
                    ffmpeg.av_packet_unref(pPacket);
                }

                // 패킷이 정상적으로 전달된 경우, 디코딩된 프레임을 가져옴
                while (true)
                {
                    error = ffmpeg.avcodec_receive_frame(pCodecContext, pFrame);

                    if (error == ffmpeg.AVERROR(ffmpeg.EAGAIN))
                    {
                        // 새로운 패킷을 보내야 함
                        break;
                    }
                    else if (error < 0 || error == ffmpeg.AVERROR_EOF)
                    {
                        return false;
                    }

                    // 하드웨어 디코딩이 활성화된 경우 데이터 변환
                    if (pCodecContext->hw_device_ctx != null)
                    {
                        ffmpeg.av_hwframe_transfer_data(receivedFrame, pFrame, 0);
                        frame = *receivedFrame;
                    }

                    frame.time_base = time_base;
                    frame.duration = pFrame->duration;
                    frame.pts = pFrame->pts;

                    return true;
                }
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
