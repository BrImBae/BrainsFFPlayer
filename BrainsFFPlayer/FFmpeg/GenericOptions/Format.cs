using MaterialDesignThemes.Wpf;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BrainsFFPlayer.FFmpeg.GenericOptions
{
    [AttributeUsage(AttributeTargets.Field)]
    public class AVFormatOptionAttribute : Attribute
    {
        public string Name { get; }
        public string Type { get; }
        public string Description { get; }

        public AVFormatOptionAttribute(string name, string type, string desc)
        {
            Name = name;
            Type = type;
            Description = desc;
        }
    }

    public enum AVFormatOption
    {
        AUTO,

        // ------------------------------- 파일 포맷 옵션 ------------------------------- //

        [AVFormatOption("mp4", "DE", "MP4 (MPEG-4 Part 14)")]
        MP4,

        [AVFormatOption("mpegts", "DE", "MPEG-TS (MPEG-2 Transport Stream)")]
        MPEG_TS,

        // ------------------------------- 네트워크 프로토콜 포맷 옵션 ------------------------------- //

        [AVFormatOption("rtp", "DE", "RTP output")]
        RTP,

        [AVFormatOption("rtsp", "DE", "RTSP output")]
        RTSP,

        // ------------------------------- 장비 포맷 옵션 ------------------------------- //

        [AVFormatOption("dshow", "Dd", "DirectShow capture")]
        DSHOW
    }
}
