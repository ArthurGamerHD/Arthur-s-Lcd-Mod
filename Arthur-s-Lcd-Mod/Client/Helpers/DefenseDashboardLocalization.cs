using LcdMod.Common.Helpers;

namespace LcdMod.Client.Helpers
{
    internal static class DefenseDashboardLocalization
    {
        const string PREFIX = Constants.MOD_PREFIX + "DefenseDashboard_";

        static string Get(string suffix)
        {
            return LocHelper.GetLoc(PREFIX + suffix);
        }

        static string Format(string suffix, params object[] args)
        {
            return string.Format(FormatingHelper.Culture, Get(suffix), args);
        }

        public static string ShieldFallback => Get("ShieldFallback");
        public static string Offline => Get("Offline");
        public static string FullyCharged => Get("FullyCharged");
        public static string RechargeInSeconds(int seconds) => Format("RechargeInSecondsFormat", seconds);
        public static string RechargeUnavailable => Get("RechargeUnavailable");
        public static string Ready => Get("Ready");
        public static string Firing => Get("Firing");
        public static string NotReady => Get("NotReady");
        public static string Disabled => Get("Disabled");
    }
}
