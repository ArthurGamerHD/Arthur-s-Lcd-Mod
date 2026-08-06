using Generated;
using LcdMod.Client.Extensions;
using LcdMod.Client.Helpers;
using LcdMod.Common.Networking;
using Sandbox.ModAPI;
using VRageMath;
using static LcdMod.Common.Helpers.Constants;

namespace LcdMod.Client.ChatCommands
{
    internal static class FactionChatCommands
    {
        /// <summary>
        /// Sets the local player's faction icon color.
        /// </summary>
        /// <loc>LcdMod_ChatCommand_FactionColor_Summary</loc>
        [ChatCommand("FactionColor")]
        public static void SetColor(Color color)
        {
            var faction = FactionHelper.GetPlayerFaction(MyAPIGateway.Session.LocalHumanPlayer.IdentityId);
            if (faction == null)
            {
                MyAPIGateway.Utilities.ShowMessage("lcdMod", LocHelper.GetLoc(MOD_PREFIX + "FactionColor_NoFaction"));
                return;
            }

            Vector3 factionColor = color.ToFactionColor();
            var packet = new PacketEditFaction(
                faction.FactionId,
                faction.Tag,
                faction.Name,
                faction.Description,
                faction.PrivateInfo,
                faction.FactionIcon.ToString(),
                faction.CustomColor,
                factionColor);

            if (MyAPIGateway.Session.IsServer)
                Common.Helpers.FactionHelperCommon.EditFaction(packet);
            else
                LcdModSessionComponent.NetworkManager.TransmitToServer(packet, false);

            MyAPIGateway.Utilities.ShowMessage("lcdMod", LocHelper.GetLoc(MOD_PREFIX + "FactionColor_Updated"));
        }
    }
}
