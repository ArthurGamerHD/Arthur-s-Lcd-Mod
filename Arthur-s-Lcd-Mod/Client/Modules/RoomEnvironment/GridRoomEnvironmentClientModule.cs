using LcdMod.Client.GridData;
using LcdMod.Common.Networking;
using Sandbox.Game.Entities;
using IMyCubeBlock = VRage.Game.ModAPI.IMyCubeBlock;

namespace LcdMod.Client.Modules.RoomEnvironment
{
    public sealed class GridRoomEnvironmentClientModule
    {
        public void HandleSyncGridRoomEnvironment(ReceivedPacketEventArgs args)
        {
            if (!args.IsFromServer)
                return;

            HandleLocalSyncGridRoomEnvironment(args.UnWrap<PacketSyncGridRoomEnvironment>());
        }

        public void HandleLocalSyncGridRoomEnvironment(PacketSyncGridRoomEnvironment packet)
        {
            if (packet == null || LcdModSessionComponent.Components == null)
                return;

            var block = MyEntities.GetEntityById(packet.BlockEntityId) as IMyCubeBlock;
            if (block == null || block.CubeGrid == null)
                return;

            GridLogic gridLogic;
            if (LcdModSessionComponent.Components.TryGetValue(block.CubeGrid.EntityId, out gridLogic))
                gridLogic.ApplyGridRoomEnvironment(packet);
        }
    }
}
