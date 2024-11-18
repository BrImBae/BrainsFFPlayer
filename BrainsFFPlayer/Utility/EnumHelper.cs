using BrainsFFPlayer.FFmpeg.GenericOptions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace BrainsFFPlayer.Utility
{
    internal static class EnumHelper
    {
        internal static AVFormatOptionAttribute? GetAVFormatOption(Enum value)
        {
            FieldInfo? field = value.GetType().GetField(value.ToString());

            return field?.GetCustomAttribute<AVFormatOptionAttribute>();
        }
    }
}
