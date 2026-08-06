using System.Linq;
using Generated;
using LcdMod.ChatCommandsGenerated;
using LcdMod.Client.Helpers;
using Sandbox.ModAPI;

namespace LcdMod.Client.ChatCommands
{
    internal static class MetaChatCommands
    {
        /// <summary>
        /// Shows the list of available chat commands.
        /// </summary>
        /// <loc>LcdMod_ChatCommand_Help_Summary</loc>
        [ChatCommand("Help")]
        public static void CommandHelp()
        {
            TextInputHelper.SpawnForLocalPlayer(
                LocHelper.GetLoc("LcdMod_ChatCommand_Help_Title"),
                null,
                CommandManager.GetReport(),
                LocHelper.GetLoc("LcdMod_ChatCommand_Help_Subtitle"),
                true);
        }
    }
}
