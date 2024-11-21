using Newtonsoft.Json;
using System.IO;

namespace BrainsFFPlayer.Utility
{
    internal static class ConfigureJsonParser
    {
        public static void SaveContainerToJson()
        {
            var container = AppData.Instance;
            var filePath = "ffconfig.json";

            string json = JsonConvert.SerializeObject(container, Formatting.Indented);
            File.WriteAllText(filePath, json);
        }

        public static bool LoadContainerFromJson()
        {
            var filePath = "ffconfig.json";

            if (File.Exists(filePath))
            {
                string json = File.ReadAllText(filePath);

                var result = JsonConvert.DeserializeObject<AppData>(json);

                if (result != null)
                {
                    AppData.Instance.VideoUrl = result.VideoUrl;
                    AppData.Instance.InputFormatIndex = result.InputFormatIndex;
                    AppData.Instance.IsHwDecoderDXVA2 = result.IsHwDecoderDXVA2;
                    AppData.Instance.FormatOptionItems = result.FormatOptionItems;

                    return true;
                }

                return false;
            }

            return false;
        }
    }
}
