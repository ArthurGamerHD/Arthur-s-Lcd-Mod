using System.Globalization;
using VRage;
using VRage.Utils;

namespace Graph.Helpers
{
    public class LocHelper
    {
        public static string Empty => GetLoc("LCDMod_Empty");


        public static string GetLoc(string key) => MyTexts.Get(MyStringId.GetOrCompute(key)).ToString();
    }
}
