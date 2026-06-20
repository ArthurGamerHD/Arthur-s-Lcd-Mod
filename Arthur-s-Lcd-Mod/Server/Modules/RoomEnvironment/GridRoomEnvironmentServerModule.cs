using System.Collections.Generic;
using LcdMod.Common.Networking;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using IMyCubeBlock = VRage.Game.ModAPI.IMyCubeBlock;
using IMyOxygenRoom = VRage.Game.ModAPI.IMyOxygenRoom;
using VRageMath;

namespace LcdMod.Server.Modules.RoomEnvironment
{
    public sealed class GridRoomEnvironmentServerModule
    {
        static readonly Vector3I[] RoomSampleDirections =
        {
            new Vector3I(-1, 0, 0),
            new Vector3I(1, 0, 0),
            new Vector3I(0, 1, 0),
            new Vector3I(0, -1, 0),
            new Vector3I(0, 0, -1),
            new Vector3I(0, 0, 1)
        };

        readonly Dictionary<ulong, Dictionary<long, long>> _requestTicks =
            new Dictionary<ulong, Dictionary<long, long>>();

        public void Unload()
        {
            _requestTicks.Clear();
        }

        public void HandleRequestGridRoomEnvironment(ReceivedPacketEventArgs args)
        {
            if (args.IsFromServer)
                return;

            HandleGridRoomEnvironmentRequest(
                args.SenderId,
                args.UnWrap<PacketRequestGridRoomEnvironment>());
        }

        public void HandleLocalRequestGridRoomEnvironment(PacketRequestGridRoomEnvironment packet)
        {
            var player = MyAPIGateway.Session?.Player;
            ulong requester = player?.SteamUserId ?? MyAPIGateway.Multiplayer.MyId;
            HandleGridRoomEnvironmentRequest(requester, packet);
        }

        void HandleGridRoomEnvironmentRequest(
            ulong requester,
            PacketRequestGridRoomEnvironment packet)
        {
            if (packet == null || !CanServeGridRoomEnvironment(requester, packet.BlockEntityId))
                return;

            var response = new PacketSyncGridRoomEnvironment
            {
                BlockEntityId = packet.BlockEntityId,
                RequestId = packet.RequestId,
                Status = GridRoomEnvironmentStatus.Unavailable
            };

            var block = MyEntities.GetEntityById(packet.BlockEntityId) as IMyCubeBlock;
            var grid = block?.CubeGrid;
            var gasSystem = grid?.GasSystem;
            if (block != null && grid != null && gasSystem != null)
            {
                if (gasSystem.IsProcessingData)
                {
                    response.Status = GridRoomEnvironmentStatus.Processing;
                }
                else
                {
                    IMyOxygenRoom room = FindGridRoom(block);
                    response.Status = GridRoomEnvironmentStatus.Available;
                    response.IsSealed = room != null && room.IsAirtight;
                    response.OxygenRatio = response.IsSealed
                        ? room.OxygenLevel(grid.GridSize)
                        : 0f;
                    response.OxygenRatio = MathHelper.Clamp(response.OxygenRatio, 0f, 1f);
                }
            }

            SendGridRoomEnvironment(requester, response);
        }

        bool CanServeGridRoomEnvironment(ulong requester, long blockEntityId)
        {
            long currentTick = MyAPIGateway.Session != null
                ? MyAPIGateway.Session.GameplayFrameCounter
                : 0L;

            Dictionary<long, long> byBlock;
            if (!_requestTicks.TryGetValue(requester, out byBlock))
            {
                byBlock = new Dictionary<long, long>();
                _requestTicks[requester] = byBlock;
            }

            long lastTick;
            if (byBlock.TryGetValue(blockEntityId, out lastTick) &&
                currentTick - lastTick < PacketRequestGridRoomEnvironment.RequestIntervalTicks)
            {
                return false;
            }

            byBlock[blockEntityId] = currentTick;
            return true;
        }

        static IMyOxygenRoom FindGridRoom(IMyCubeBlock block)
        {
            var grid = block?.CubeGrid;
            var gasSystem = grid?.GasSystem;
            if (gasSystem == null)
                return null;

            var oxygenBlock = gasSystem.GetOxygenBlock(block.GetPosition());
            if (oxygenBlock?.Room != null)
                return oxygenBlock.Room;

            Vector3I origin = block.Position;
            IMyOxygenRoom bestRoom = null;
            float bestLevel = -1f;
            bool bestIsAirtight = false;

            for (int i = 0; i < RoomSampleDirections.Length; i++)
            {
                Vector3I adjacent = origin + RoomSampleDirections[i];
                IMyOxygenRoom room = gasSystem.GetOxygenRoomForCubeGridPosition(ref adjacent);
                if (room == null)
                    continue;

                float level = room.IsAirtight
                    ? room.OxygenLevel(grid.GridSize)
                    : 0f;
                bool isBetter = bestRoom == null ||
                                (room.IsAirtight && !bestIsAirtight) ||
                                (room.IsAirtight == bestIsAirtight && level > bestLevel);
                if (!isBetter)
                    continue;

                bestRoom = room;
                bestLevel = level;
                bestIsAirtight = room.IsAirtight;
            }

            return bestRoom;
        }

        static void SendGridRoomEnvironment(
            ulong requester,
            PacketSyncGridRoomEnvironment packet)
        {
            var localSteamId = MyAPIGateway.Session?.Player?.SteamUserId ?? 0;
            var clientRoomEnvironment = LcdModSessionComponent.Client?.RoomEnvironment;
            if (clientRoomEnvironment != null &&
                (requester == localSteamId ||
                 (localSteamId == 0 && requester == MyAPIGateway.Multiplayer.MyId)))
            {
                clientRoomEnvironment.HandleLocalSyncGridRoomEnvironment(packet);
                return;
            }

            if (requester != 0)
                LcdModSessionComponent.NetworkManager.TransmitToPlayer(packet, requester);
        }
    }
}
