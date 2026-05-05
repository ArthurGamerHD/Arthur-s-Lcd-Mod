using LcdMod.Common.Config;
using LcdMod.Common.Helpers;
using LcdMod.Common.Networking;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRageMath;

namespace LcdMod.Server
{
    public sealed class LcdModServerComponent
    {
        readonly LcdModSessionComponent _session;

        public LcdModServerComponent(LcdModSessionComponent session)
        {
            _session = session;
        }

        public void LoadData()
        {
            MyAPIGateway.Entities.OnEntityAdd += EntityAdded;
        }

        public void UnloadData()
        {
            MyAPIGateway.Entities.OnEntityAdd -= EntityAdded;
        }

        public void HandleSyncConfig(ReceivedPacketEventArgs args)
        {
            var packet = args.UnWrap<NetworkPackageSyncScreenConfig>();
            var block = MyEntities.GetEntityById(packet.BlockId) as IMyFunctionalBlock;
            if (block == null)
                return;

            RemapHelper.PinBlocks(packet.Config);
            ScreenProviderConfigStorage.Save(block, packet.Config);
        }

        void EntityAdded(IMyEntity entity)
        {
            try
            {
                var grid = entity as IMyCubeGrid;
                if (grid == null)
                    return;

                RemapHelper.RemapGrid(grid);
            }
            catch (System.Exception e)
            {
                ErrorHandlerHelper.LogError(e, _session);
            }
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

            FactionHelperCommon.EditFaction(packet);
        }
    }
}
