using BrainsFFPlayer.FFmpeg.GenericOptions;
using System.Reflection;

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
