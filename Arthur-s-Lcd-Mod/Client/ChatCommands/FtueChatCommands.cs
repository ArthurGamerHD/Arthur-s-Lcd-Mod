using System.Text;
using Generated;
using LcdMod.Client.Config;
using LcdMod.Client.Helpers;
using Sandbox.ModAPI;

namespace LcdMod.Client.ChatCommands
{
    internal static class FtueChatCommands
    {
        /// <summary>
        /// Resets completed first-time user experience tips.
        /// </summary>
        /// <loc>LcdMod_ChatCommand_ResetFtue_Summary</loc>
        [ChatCommand("ResetFtue")]
        public static void Reset(string tip = "")
        {
            var client = LcdModSessionComponent.Client;
            if (client == null || client.Ftue == null)
                return;

            if (string.IsNullOrEmpty(tip))
                client.Ftue.ResetCommand();
            else
            {
                if (LocalConfigManager.ClearCompletedFtueTip(tip))
                {
                    MyAPIGateway.Utilities.ShowMessage(
                        "lcdMod",
                        string.Format(
                            LocHelper.GetLoc("LcdMod_ChatCommand_ResetFtue_Cleared_Message_Format"),
                            tip));
                }
                else
                {

                    if (LocalConfigManager.Config?.CompletedFtueTips?.Count > 0)
                    {
                        var sb = new StringBuilder();
                        foreach (var completed in LocalConfigManager.Config.CompletedFtueTips)
                        {
                            sb.Append(completed + ", ");
                        }
                        
                        MyAPIGateway.Utilities.ShowMessage(
                            "lcdMod",
                            string.Format(
                                LocHelper.GetLoc("LcdMod_ChatCommand_ResetFtue_NotFound_Message_Format"),
                                tip,
                                sb.ToString().TrimEnd(',', ' ')));
                    }
                    else
                    {
                        MyAPIGateway.Utilities.ShowMessage(
                            "lcdMod",
                            LocHelper.GetLoc("LcdMod_ChatCommand_ResetFtue_NoneFound_Message"));
                    }
                }
            }
        }
    }
}