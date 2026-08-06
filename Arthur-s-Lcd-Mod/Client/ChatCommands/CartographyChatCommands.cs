#if DEBUG
using Generated;

namespace LcdMod.Client.ChatCommands
{
    internal static class CartographyChatCommands
    {
        /// <summary>
        /// Tests loaded planet cartography definitions and writes diagnostic archives.
        /// </summary>
        /// <loc>LcdMod_ChatCommand_TestCartography_Summary</loc>
        [ChatCommand("TestCartography")]
        public static void TestCartography(string[] args)
        {
            var client = LcdModSessionComponent.Client;
            if (client == null || client.CartographyDebugReport == null)
                return;

            client.CartographyDebugReport.RunCommand(args);
        }
    }
}
#endif
