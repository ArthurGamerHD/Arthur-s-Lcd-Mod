using Graph.Helpers;
using Graph.Networking;
using Graph.System.Config;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRageMath;

namespace Graph.System
{
    public partial class LcdModSessionComponent
    {
        sealed class LcdModServerComponent
        {
            readonly LcdModSessionComponent _session;

            public LcdModServerComponent(LcdModSessionComponent session)
            {
                _session = session;
            }

            public void UnloadData()
            {
            }

            public void HandleSyncConfig(ReceivedPacketEventArgs args)
            {
                var packet = args.UnWrap<NetworkPackageSyncScreenConfig>();
                var block = MyEntities.GetEntityById(packet.BlockId) as IMyFunctionalBlock;
                if (block == null)
                    return;

                ScreenProviderConfigStorage.Save(block, packet.Config);
            }

            public void HandleEditFaction(ReceivedPacketEventArgs args)
            {
                var packet = args.UnWrap<PacketEditFaction>();
                var sender = MyAPIGateway.Players.TryGetIdentityId(args.SenderId);
                var faction = MyAPIGateway.Session.Factions.TryGetPlayerFaction(sender);

                if (faction == null || packet.FactionId != faction.FactionId || !(faction.IsLeader(sender) || faction.IsFounder(sender)))
                {
                    MyVisualScriptLogicProvider.SendChatMessageColored("Unable to edit faction", Color.Red, "Error", sender);
                    return;
                }

                FactionHelper.EditFaction(packet);
            }
        }
    }
}
