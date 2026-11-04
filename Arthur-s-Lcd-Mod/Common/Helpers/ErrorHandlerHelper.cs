using System;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Utils;

namespace LcdMod.Common.Helpers
{
    public class ErrorHandlerHelper
    {
        const string ModName = "Arthur's LCD Mod";

        public static void LogError(Exception error, object source) => LogError(error, source?.GetType());

        public static void LogError(Exception error, Type source)
        {
            if (source == null)
                source = typeof(ErrorHandlerHelper);

            // Clients report through Acrux UI; a dedicated server never connects to it.
            if (Acrux.Toolkit.Ui.IsLoaded)
            {
                Acrux.Diagnostics.ErrorReporter.LogError(ModName, error, source);
                return;
            }

            MyLog.Default.WriteLineAndConsole(error.ToString());
            if (MyAPIGateway.Session.Player != null)
                MyAPIGateway.Utilities.ShowNotification(
                    $"[ ERROR: {source.FullName}: {error.Message} | Send SpaceEngineers.Log to mod author ]", 10000,
                    MyFontEnum.Red);
        }
    }
}
