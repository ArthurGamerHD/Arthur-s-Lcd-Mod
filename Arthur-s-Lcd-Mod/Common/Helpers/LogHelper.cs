using System;
using System.Collections.Generic;
using VRage.Utils;

namespace LcdMod.Common.Helpers
{
    public static class LogHelper
    {
        static readonly HashSet<string> LoggedOnce = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public static void LogOnce(string key, string message)
        {
            if (!LoggedOnce.Add(key))
                return;

            LogInfo(message);
        }
        
        public static void LogInfo(string message) => Log(MyLogSeverity.Info, message);
        public static void Log(MyLogSeverity severity, string message)
        {
            MyLog.Default.Log(severity,$"[{nameof(LcdMod)}] " + message.Replace("{", "{{").Replace("}", "}}"));
        }
        
        public static void Log(MyLogSeverity severity, string message, params object[] args)
        {
            MyLog.Default.Log(severity,$"[{nameof(LcdMod)}] " + message, args);
        }
    }
}