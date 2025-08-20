using FFmpeg.AutoGen;

using System.IO;

namespace BrainsFFPlayer.FFmpeg.Core
{
    internal unsafe class H264VideoStreamEncoder : IDisposable
    {
        private AVFormatContext* oFormatContext;
        private AVCodecContext* oCodecContext;
        private AVCodec* oCodec;
        private AVStream* outStream;
        private bool isHeaderWritten;

        public void OpenOutputURL(string fileName, VideoInfo videoInfo)
        {
            AVFormatContext* fmt = null;
            AVCodecContext* c = null;
            AVStream* st = null;
            AVDictionary* codecOpts = null;

            try
            {
                // 1) 출력 컨텍스트 생성
                int ret = ffmpeg.avformat_alloc_output_context2(&fmt, null, null, fileName);
                if (ret < 0 || fmt == null)
                    throw new InvalidOperationException($"Failed to allocate output format context: {ret}");

                // 2) 인코더 찾기
                oCodec = ffmpeg.avcodec_find_encoder(AVCodecID.AV_CODEC_ID_H264);
                if (oCodec == null)
                    throw new InvalidOperationException("H.264 encoder not found.");

                // 3) 새 스트림 생성 (반환값을 로컬→필드로 반드시 대입)
                st = ffmpeg.avformat_new_stream(fmt, oCodec);
                if (st == null)
                    throw new InvalidOperationException("Could not create stream.");
                // Stream time_base 먼저 지정(컨테이너/인코더 정책에 맞춤)
                // 보통 인코더 time_base(= 1/fps)와 동일하게.
                st->time_base = videoInfo.Timebase;

                // 4) 코덱 컨텍스트 생성 및 설정
                c = ffmpeg.avcodec_alloc_context3(oCodec);
                if (c == null)
                    throw new InvalidOperationException("Could not allocate codec context.");

                c->codec_id = oCodec->id;
                c->codec_type = AVMediaType.AVMEDIA_TYPE_VIDEO;
                c->width = videoInfo.FrameSize.Width;
                c->height = videoInfo.FrameSize.Height;
                c->gop_size = videoInfo.GopSize;
                c->max_b_frames = videoInfo.MaxBFrames;
                c->bit_rate = videoInfo.Bitrate;
                c->sample_aspect_ratio = videoInfo.SampleAspectRatio;

                // time_base / framerate 설정
                // 보통 time_base = 1/framerate
                c->time_base = new AVRational { num = 1, den = videoInfo.Timebase.den };   // 예: {1,30}
                c->framerate = new AVRational { num = videoInfo.FrameRate.num, den = 1 };   // 예: {30,1}

                // 픽셀 포맷
                c->pix_fmt = AVPixelFormat.AV_PIX_FMT_YUV420P;

                // 글로벌 헤더 필요 시 플래그 설정
                if ((fmt->oformat->flags & ffmpeg.AVFMT_GLOBALHEADER) != 0)
                    c->flags |= ffmpeg.AV_CODEC_FLAG_GLOBAL_HEADER;

                // 5) 프로파일/레벨 등 옵션
                ffmpeg.av_dict_set(&codecOpts, "profile", "high", 0);
                ffmpeg.av_dict_set(&codecOpts, "level", "4.0", 0);
                // 저지연이 더 필요하면:
                // ffmpeg.av_dict_set(&codecOpts, "tune", "zerolatency", 0);
                // ffmpeg.av_dict_set(&codecOpts, "preset", "veryfast", 0);

                // 6) 코덱 열기
                ffmpeg.avcodec_open2(c, oCodec, &codecOpts).ThrowExceptionIfError();
                // codecOpts는 avcodec_open2 내부에서 소거/소비됨. 그래도 혹시 남아있으면 프리
                if (codecOpts != null) ffmpeg.av_dict_free(&codecOpts);

                // 7) 스트림 파라미터에 코덱 설정 복사
                ffmpeg.avcodec_parameters_from_context(st->codecpar, c).ThrowExceptionIfError();
                // 스트림 time_base 재확인(보통 인코더 컨텍스트와 동일)
                st->time_base = c->time_base;

                // 8) 출력 IO 열기 (파일 없는 포맷은 생략)
                if ((fmt->oformat->flags & ffmpeg.AVFMT_NOFILE) == 0)
                {
                    ret = ffmpeg.avio_open(&fmt->pb, fileName, ffmpeg.AVIO_FLAG_WRITE);
                    if (ret < 0)
                        throw new IOException($"Failed to open output file: {fileName} (ret={ret})");
                }

                // 9) 헤더 작성
                ffmpeg.av_dump_format(fmt, 0, fileName, 1);
                ffmpeg.avformat_write_header(fmt, null).ThrowExceptionIfError();
                isHeaderWritten = true;

                // 10) 필드에 반영 (성공 시점에만)
                oFormatContext = fmt;
                oCodecContext = c;
                outStream = st;
            }
            catch
            {
                if (codecOpts != null) ffmpeg.av_dict_free(&codecOpts);

                // 실패 시 안전 정리 (열린 순서의 반대)
                if (fmt != null && isHeaderWritten)
                    ffmpeg.av_write_trailer(fmt);

                if (fmt != null && (fmt->oformat->flags & ffmpeg.AVFMT_NOFILE) == 0 && fmt->pb != null)
                    ffmpeg.avio_closep(&fmt->pb);

                if (c != null)
                    ffmpeg.avcodec_free_context(&c);

                if (fmt != null)
                    ffmpeg.avformat_free_context(fmt);

                throw;
            }
        }

        public void TryEncodeNextPacket(AVFrame frame)
        {
            if (oCodecContext == null || oFormatContext == null || outStream == null)
                throw new InvalidOperationException("Encoder not initialized.");

            // 1) 프레임 전송
            ffmpeg.avcodec_send_frame(oCodecContext, &frame).ThrowExceptionIfError();

            // 2) 패킷 수신/쓰기
            AVPacket* pkt = ffmpeg.av_packet_alloc();
            if (pkt == null) throw new OutOfMemoryException("av_packet_alloc failed.");

            try
            {
                while (true)
                {
                    int ret = ffmpeg.avcodec_receive_packet(oCodecContext, pkt);
                    if (ret == ffmpeg.AVERROR(ffmpeg.EAGAIN) || ret == ffmpeg.AVERROR_EOF)
                        break;
                    ret.ThrowExceptionIfError();

                    // 스트림 인덱스/타임스탬프 스케일
                    pkt->stream_index = outStream->index;
                    ffmpeg.av_packet_rescale_ts(pkt, oCodecContext->time_base, outStream->time_base);
                    pkt->pos = -1;

                    ffmpeg.av_interleaved_write_frame(oFormatContext, pkt).ThrowExceptionIfError();
                    ffmpeg.av_packet_unref(pkt);
                }
            }
            finally
            {
                ffmpeg.av_packet_free(&pkt);
            }
        }

        public void FlushEncode()
        {
            if (oCodecContext == null) return;

            // flush: NULL frame 전송 후 남은 패킷 모두 받기
            ffmpeg.avcodec_send_frame(oCodecContext, null).ThrowExceptionIfError();

            AVPacket* pkt = ffmpeg.av_packet_alloc();
            if (pkt == null) throw new OutOfMemoryException("av_packet_alloc failed.");

            try
            {
                while (true)
                {
                    int ret = ffmpeg.avcodec_receive_packet(oCodecContext, pkt);
                    if (ret == ffmpeg.AVERROR_EOF || ret == ffmpeg.AVERROR(ffmpeg.EAGAIN))
                        break;
                    ret.ThrowExceptionIfError();

                    pkt->stream_index = outStream->index;
                    ffmpeg.av_packet_rescale_ts(pkt, oCodecContext->time_base, outStream->time_base);
                    pkt->pos = -1;

                    ffmpeg.av_interleaved_write_frame(oFormatContext, pkt).ThrowExceptionIfError();
                    ffmpeg.av_packet_unref(pkt);
                }
            }
            finally
            {
                ffmpeg.av_packet_free(&pkt);
            }
        }

        #region Dispose

        public void Dispose()
        {
            // 안전하게 트레일러/클로즈/프리
            if (oFormatContext != null && isHeaderWritten)
            {
                ffmpeg.av_write_trailer(oFormatContext);
                isHeaderWritten = false;
            }

            if (oFormatContext != null)
            {
                if ((oFormatContext->oformat->flags & ffmpeg.AVFMT_NOFILE) == 0 && oFormatContext->pb != null)
                    ffmpeg.avio_closep(&oFormatContext->pb);

                ffmpeg.avformat_free_context(oFormatContext);
                oFormatContext = null;
            }

            if (oCodecContext != null)
            {
                ffmpeg.av_free(oCodecContext);
                oCodecContext = null;
            }

            // oCodec / outStream은 포인터 레퍼런스이므로 별도 해제 불필요
            oCodec = null;
            outStream = null;
        }

        #endregion
    }

}
