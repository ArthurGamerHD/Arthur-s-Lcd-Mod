#if DEBUG || EXPERIMENTAL
using System.Linq;
using Generated;
using LcdMod.Client.Helpers;
using LcdMod.Common.Helpers;
using Sandbox.ModAPI;

namespace LcdMod.Client.ChatCommands
{
    internal static class DiagnosticsChatCommands
    {
#if DEBUG
        /// <summary>
        /// Opens a diagnostic text input dialog.
        /// </summary>
        /// <loc>LcdMod_ChatCommand_TextInput_Summary</loc>
        [ChatCommand("TextInput")]
        public static void TextInput(string[] strings)
        {
            TextInputHelper.SpawnForLocalPlayer(
                strings.FirstOrDefault(),
                s => MyAPIGateway.Utilities.ShowNotification("User typed: " + s),
                "Hello World!",
                strings.Length > 1 ? strings[1] : string.Empty);
        }

        /// <summary>
        /// Opens or closes the retained ADK visual-tree debugger for the last targeted surface.
        /// </summary>
        [ChatCommand("VisibleTreeDebug")]
        public static void VisibleTreeDebug(bool? enabled)
        {
            var service = LcdModSessionComponent.Client?.VisibleTreeDebug;
            if (service == null)
                return;
            bool open = enabled ?? !service.IsOpen;
            if (open)
                service.Open();
            else
                service.Close();
        }

        /// <summary>
        /// Redraws every reused retained LCD frame and reports elements that changed without invalidating.
        /// </summary>
        [ChatCommand("VerifyFrameCache")]
        public static void VerifyFrameCache(bool? enabled)
        {
            bool enable = enabled ?? Adk.Gui.Lcd.LcdGuiHost.FrameCacheMismatch == null;
            Adk.Gui.Lcd.LcdGuiHost.FrameCacheMismatch = enable ? ReportStaleFrame : (System.Action<string>)null;
            MyAPIGateway.Utilities.ShowMessage(
                "lcdMod",
                enable
                    ? LocHelper.GetLoc("LcdMod_ChatCommand_VerifyFrameCache_Enabled_Message")
                    : LocHelper.GetLoc("LcdMod_ChatCommand_VerifyFrameCache_Disabled_Message"));
        }

        /// <summary>
        /// Toggles the parsed style cache. While disabled, every new view reads its styles from disk again.
        /// </summary>
        [ChatCommand("StyleCache")]
        public static void StyleCache(bool? enabled)
        {
            bool enable = enabled ?? !Adk.Gui.Styling.StyleSheet.CacheSources;
            Adk.Gui.Styling.StyleSheet.CacheSources = enable;
            MyAPIGateway.Utilities.ShowMessage(
                "lcdMod",
                enable
                    ? LocHelper.GetLoc("LcdMod_ChatCommand_StyleCache_Enabled_Message")
                    : LocHelper.GetLoc("LcdMod_ChatCommand_StyleCache_Disabled_Message"));
        }

        /// <summary>
        /// Toggles view hot reload. While enabled, edited view and style XAML in the mod folder is rebuilt in place.
        /// </summary>
        [ChatCommand("HotReload")]
        public static void HotReloadViews(bool? enabled)
        {
            bool enable = enabled ?? !Adk.Gui.Markup.HotReload.Enabled;
            Adk.Gui.Markup.HotReload.Enabled = enable;
            if (!enable)
            {
                MyAPIGateway.Utilities.ShowMessage(
                    "lcdMod",
                    LocHelper.GetLoc("LcdMod_ChatCommand_HotReload_Disabled_Message"));
                return;
            }
            int reloaded = Adk.Gui.Markup.HotReload.ReloadAll();
            MyAPIGateway.Utilities.ShowMessage(
                "lcdMod",
                string.Format(
                    LocHelper.GetLoc("LcdMod_ChatCommand_HotReload_Enabled_Message"),
                    reloaded,
                    Adk.Gui.Markup.HotReload.TrackedViewCount));
        }

        static void ReportStaleFrame(string message)
        {
            LogHelper.Log(VRage.Utils.MyLogSeverity.Warning, message);
            MyAPIGateway.Utilities.ShowNotification(message, 2000, "Red");
        }
#endif

    }
}
#endif
