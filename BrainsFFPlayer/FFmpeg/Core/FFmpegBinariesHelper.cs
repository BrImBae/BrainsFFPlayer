﻿using FFmpeg.AutoGen;
using System.IO;
using System.Runtime.InteropServices;

namespace BrainsFFPlayer.FFmpeg.Core
{
    internal class FFmpegBinariesHelper
    {
        internal static void RegisterFFmpegBinaries()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var current = Environment.CurrentDirectory;
                var probe = Path.Combine("Plugins", "FFmpeg", Environment.Is64BitProcess ? "x64" : "x86");

                while (current != null)
                {
                    var ffmpegBinaryPath = Path.Combine(current, probe);

                    if (Directory.Exists(ffmpegBinaryPath))
                    {
                        ffmpeg.RootPath = ffmpegBinaryPath;
                        return;
                    }

                    current = Directory.GetParent(current)?.FullName;
                }
            }
            else
            {
                throw new NotSupportedException(); // fell free add support for platform of you choose
            }
        }
    }
}
