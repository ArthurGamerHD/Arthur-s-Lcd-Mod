using System.Collections.Generic;

namespace LcdMod.Common.Config.Components
{
    public static class GeneralConfigCustomDataExtensions
    {
        public static byte[] GetCustomData(this GeneralConfigComponent config, string key)
        {
            byte[] value;
            return config != null && !string.IsNullOrEmpty(key) && config.CustomData != null &&
                   config.CustomData.TryGetValue(key, out value)
                ? value
                : null;
        }

        public static void SetCustomData(this GeneralConfigComponent config, string key, byte[] data)
        {
            if (config == null || string.IsNullOrEmpty(key))
                return;

            if (config.CustomData == null)
                config.CustomData = new Dictionary<string, byte[]>();

            if (data == null)
                config.CustomData.Remove(key);
            else
                config.CustomData[key] = data;
        }
    }
}
