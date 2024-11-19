using BrainsCV.MotionImageryStandardsBoard;
using BrainsFFPlayer.FFmpeg.GenericOptions;
using BrainsFFPlayer.Utility;
using FFmpeg.AutoGen;
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

        public VideoStreamDecoder(string url, AVFormatOption avInputOption, AVHWDeviceType HWDeviceType = AVHWDeviceType.AV_HWDEVICE_TYPE_NONE)
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
                    iformat = ffmpeg.av_find_input_format(inputFormat.Name);
                }

                AVDictionary* options;

                ffmpeg.av_dict_set(&options, "max_delay", "0", 0);
                ffmpeg.av_dict_set(&options, "rtbufsize", "0", 0);
                ffmpeg.av_dict_set(&options, "thread_queue_size", "0", 0); //set the maximum number of queued packets from the demuxer

                switch (avInputOption)
                {
                    case AVFormatOption.AUTO:
                        ffmpeg.avformat_open_input(&pFormat, url, null, &options).ThrowExceptionIfError();
                        break;
                    case AVFormatOption.MP4:
                    case AVFormatOption.MPEG_TS:
                    case AVFormatOption.DSHOW:
                        ffmpeg.avformat_open_input(&pFormat, url, iformat, &options).ThrowExceptionIfError();
                        break;
                    case AVFormatOption.RTP:
                    case AVFormatOption.RTSP:                       
                        ffmpeg.av_dict_set(&options, "fflags", "nobuffer", 0);          //udp, rtp, rtsp에만 설정 가능
                        ffmpeg.av_dict_set(&options, "reorder_queue_size", "1", 0);     // udp, rtp, rtsp에만 설정 가능
                        ffmpeg.avformat_open_input(&pFormat, url, iformat, &options).ThrowExceptionIfError();
                        break;
                }

                ffmpeg.av_dict_free(&options);

                ffmpeg.avformat_find_stream_info(pFormatContext, null).ThrowExceptionIfError();

                AVCodec* codec = null;

                videoIndex = ffmpeg.av_find_best_stream(pFormatContext, AVMediaType.AVMEDIA_TYPE_VIDEO, -1, -1, &codec, 0).ThrowExceptionIfError();
                dataIndex = ffmpeg.av_find_best_stream(pFormatContext, AVMediaType.AVMEDIA_TYPE_DATA, -1, -1, null, 0);

                pCodecContext = ffmpeg.avcodec_alloc_context3(codec);

                ffmpeg.av_opt_set(pCodecContext->priv_data, "preset", "fast", 0);
                ffmpeg.av_opt_set(pCodecContext->priv_data, "tune", "zerolatency", 0);

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

            ffmpeg.av_frame_unref(pFrame);
            ffmpeg.av_frame_unref(receivedFrame);
            int error;

            AVRational time_base = pFormatContext->streams[videoIndex]->time_base;

            do
            {
                try
                {
                    do
                    {
                        ffmpeg.av_packet_unref(pPacket);

                        error = ffmpeg.av_read_frame(pFormatContext, pPacket).ThrowExceptionIfError();

                        if (error == ffmpeg.AVERROR_EOF)
                        {
                            frame = *pFrame;
                            return false;
                        }

                        if (dataIndex >= 0 && pFormatContext->streams[dataIndex]->codecpar->codec_type == AVMediaType.AVMEDIA_TYPE_DATA)
                        {
                            if(MISB.IsMISBMetadata(pPacket->size, pPacket->data))
                            {
                                vmti = MISB.ProcessPacketMetadata(pPacket->size, pPacket->data);
                            }                           
                        }

                    } while (pPacket->stream_index != videoIndex);

                    /* Send the video frame stored in the temporary packet to the decoder.
                     * The input video stream decoder is used to do this. */
                    ffmpeg.avcodec_send_packet(pCodecContext, pPacket).ThrowExceptionIfError();
                }
                finally
                {
                    ffmpeg.av_packet_unref(pPacket);
                }

                //read decoded frame from input codec context
                error = ffmpeg.avcodec_receive_frame(pCodecContext, pFrame).ThrowExceptionIfError();

            } while (error == ffmpeg.AVERROR(ffmpeg.EAGAIN));

            if (pCodecContext->hw_device_ctx != null)
            {
                ffmpeg.av_hwframe_transfer_data(receivedFrame, pFrame, 0).ThrowExceptionIfError();
                frame = *receivedFrame;
            }
            else
            {
                frame = *pFrame;
            }

            frame.time_base = time_base;
            frame.duration = pFrame->duration;
            frame.pts = pFrame->pts;

            return true;
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
