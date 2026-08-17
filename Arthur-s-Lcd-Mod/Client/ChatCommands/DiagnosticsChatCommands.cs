#if DEBUG || EXPERIMENTAL
using System.Linq;
using Generated;
using LcdMod.Client.Helpers;
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
#endif

#if EXPERIMENTAL
        /// <summary>
        /// Profiles LCD app rendering and named runtime/event work for a specified duration.
        /// </summary>
        /// <loc>LcdMod_ChatCommand_Profile_Summary</loc>
        [ChatCommand("Profile")]
        public static void Profile(string[] args)
        {
            var client = LcdModSessionComponent.Client;
            if (client == null || client.AppRunProfiler == null)
                return;

            client.AppRunProfiler.RunCommand(args);
        }
#endif
    }
}
#endif
