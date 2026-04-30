using System;
using System.Collections.Generic;
using VRage.Utils;

namespace Graph.Helpers
{
    public static class LogHelper
    {
        static readonly HashSet<string> LoggedOnce = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public static void LogOnce(string key, string message)
        {
            if (!LoggedOnce.Add(key))
                return;

            Log(message);
        }
        
        static void Log(string message)
        {
            MyLog.Default.WriteLine("[LCDMod] " + message);
        }
    }
}