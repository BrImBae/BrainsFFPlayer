
namespace BrainsFFPlayer.FFmpeg.GenericOptions
{
    [AttributeUsage(AttributeTargets.Field)]
    public class AVFormatOptionAttribute : Attribute
    {
        public string Key { get; }
        public string Value { get; }
        public string Description { get; }

        public AVFormatOptionAttribute(string key, string value, string desc)
        {
            Key = key;
            Value = value;
            Description = desc;
        }
    }

    public enum AVInputOption
    {
        AUTO,

        // ------------------------------- 파일 포맷 옵션 ------------------------------- //

        [AVFormatOption("mp4", "", "MP4 (MPEG-4 Part 14)")]
        MP4,

        [AVFormatOption("mpegts", "", "MPEG-TS (MPEG-2 Transport Stream)")]
        MPEG_TS,

        // ------------------------------- 네트워크 프로토콜 포맷 옵션 ------------------------------- //

        [AVFormatOption("rtp", "", "RTP output")]
        RTP,

        [AVFormatOption("rtsp", "", "RTSP output")]
        RTSP,

        // ------------------------------- 장비 포맷 옵션 ------------------------------- //

        [AVFormatOption("dshow", "", "DirectShow capture")]
        DSHOW
    }

    public enum AVFormatOption
    {
        // ------------------------------- 공통 ------------------------------- //

        [AVFormatOption("max_delay", "-1", "maximum muxing or demuxing delay in microseconds (from -1 to INT_MAX) (default -1)")]
        MAX_DELAY,

        [AVFormatOption("rtbufsize", "3041280", "max memory used for buffering real-time frames (from 0 to INT_MAX) (default 3041280)")]
        RTBUFSIZE,

        // ---------------------------- RTP / RTSP ---------------------------- //

        [AVFormatOption("timeout", "1000000", "Set timeout for socket I/O operations expressed in seconds (fractional value can be set). Applicable only for HTTP output. (us)")]
        TIMEOUT,

        [AVFormatOption("fflags", "nobuffer", "nobuffer: Reduce the latency introduced by buffering during initial input streams analysis.")]
        FFLAGS,

        [AVFormatOption("reorder_queue_size", "-1", "set number of packets to buffer for handling of reordered packets (from -1 to INT_MAX) (default -1)")]
        REORDER_QUEUE_SIZE,

        [AVFormatOption("overrun_nonfatal", "0", "survive in case of receiving fifo buffer overrun (default false)")]
        OVERRUN_NONFATAL,

        [AVFormatOption("probesize", "5000000", "set probing size (from 32 to I64_MAX) (default 5000000)")]
        PROBESIZE
    }
}
