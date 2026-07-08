using System.Collections.Generic;
using LcdMod.Common.Networking;
using Sandbox.ModAPI;
using IMyCubeBlock = VRage.Game.ModAPI.IMyCubeBlock;
using VRageMath;

namespace LcdMod.Client.GridData
{
    internal struct GridRoomEnvironmentSample
    {
        public bool IsSealed;
        public float OxygenRatio;
    }

    public partial class GridLogic
    {
        sealed class GridRoomEnvironmentCacheEntry
        {
            public bool HasSample;
            public GridRoomEnvironmentSample Sample;
            public long LastRequestTick = -PacketRequestGridRoomEnvironment.REQUEST_INTERVAL_TICKS;
            public uint PendingRequestId;
        }

        readonly Dictionary<long, GridRoomEnvironmentCacheEntry> _roomEnvironmentByBlock =
            new Dictionary<long, GridRoomEnvironmentCacheEntry>();

        uint _nextRoomEnvironmentRequestId;

        internal bool TryGetGridRoomEnvironment(
            IMyCubeBlock block,
            out GridRoomEnvironmentSample sample)
        {
            sample = default(GridRoomEnvironmentSample);
            if (block == null || block.MarkedForClose || block.CubeGrid == null ||
                block.CubeGrid.EntityId != Grid.EntityId || MyAPIGateway.Session == null)
            {
                return false;
            }

            GridRoomEnvironmentCacheEntry entry;
            if (!_roomEnvironmentByBlock.TryGetValue(block.EntityId, out entry))
            {
                entry = new GridRoomEnvironmentCacheEntry();
                _roomEnvironmentByBlock[block.EntityId] = entry;
            }

            long currentTick = MyAPIGateway.Session.GameplayFrameCounter;
            if (currentTick - entry.LastRequestTick >= PacketRequestGridRoomEnvironment.REQUEST_INTERVAL_TICKS)
                RequestGridRoomEnvironment(block, entry, currentTick);

            if (!entry.HasSample)
                return false;

            sample = entry.Sample;
            return true;
        }

        void RequestGridRoomEnvironment(
            IMyCubeBlock block,
            GridRoomEnvironmentCacheEntry entry,
            long currentTick)
        {
            uint requestId = ++_nextRoomEnvironmentRequestId;
            if (requestId == 0)
                requestId = ++_nextRoomEnvironmentRequestId;

            entry.LastRequestTick = currentTick;
            entry.PendingRequestId = requestId;

            var packet = new PacketRequestGridRoomEnvironment
            {
                BlockEntityId = block.EntityId,
                RequestId = requestId
            };

            if (MyAPIGateway.Session.IsServer && LcdModSessionComponent.Server != null)
            {
                LcdModSessionComponent.Server.RoomEnvironment.HandleLocalRequestGridRoomEnvironment(packet);
                return;
            }

            if (LcdModSessionComponent.NetworkManager != null)
                LcdModSessionComponent.NetworkManager.TransmitToServer(packet, false);
        }

        internal void ApplyGridRoomEnvironment(PacketSyncGridRoomEnvironment packet)
        {
            if (packet == null)
                return;

            GridRoomEnvironmentCacheEntry entry;
            if (!_roomEnvironmentByBlock.TryGetValue(packet.BlockEntityId, out entry) ||
                entry.PendingRequestId != packet.RequestId)
            {
                return;
            }

            if (packet.Status != GridRoomEnvironmentStatus.Available)
                return;

            entry.Sample = new GridRoomEnvironmentSample
            {
                IsSealed = packet.IsSealed,
                OxygenRatio = MathHelper.Clamp(packet.OxygenRatio, 0f, 1f)
            };
            entry.HasSample = true;
        }
    }
}
