using VRage;
using VRage.Utils;

namespace LcdMod.Client.Helpers
{
    public static class LocHelper
    {
        public static string Empty => GetLoc("LcdMod_Empty");

        public static string GetLoc(string key) => MyTexts.Get(MyStringId.GetOrCompute(key)).ToString();
    }
}
