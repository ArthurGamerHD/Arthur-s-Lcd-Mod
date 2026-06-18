using LcdMod.Common.Helpers;
using VRage;
using VRage.Utils;
using static LcdMod.Common.Helpers.Constants;

namespace LcdMod.Client.Helpers
{
    public static class LocHelper
    {
        public static string Empty => GetLoc(MOD_PREFIX + "Empty");

        public static string GetLoc(string key) => MyTexts.Get(MyStringId.GetOrCompute(key)).ToString();
    }
}
