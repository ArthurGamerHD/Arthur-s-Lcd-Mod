using VRage;
using VRage.Game;
using VRage.Utils;
using static LcdMod.Common.Helpers.Constants;

namespace LcdMod.Client.Helpers
{
    public static class LocHelper
    {
        public static string Empty => GetLoc(MOD_PREFIX + "Empty");
        public static string Disabled => GetLoc("Disabled");

        public static string GetLoc(string key) => MyTexts.Get(MyStringId.GetOrCompute(key)).ToString();
        
        public static string GetTemperatureLevelText(MyTemperatureLevel level)
        {
            switch (level)
            {
                case MyTemperatureLevel.ExtremeFreeze: return GetLoc("TemperatureFreeze");
                case MyTemperatureLevel.Freeze: return GetLoc("TemperatureCold");
                case MyTemperatureLevel.Cozy: return GetLoc("TemperatureWarm");
                case MyTemperatureLevel.Hot: return GetLoc("TemperatureHot");
                case MyTemperatureLevel.ExtremeHot: return GetLoc("TemperatureInferno");
                default: return GetLoc(MOD_PREFIX + "Common_Value_Unavailable");
            }
        }
    }
}
