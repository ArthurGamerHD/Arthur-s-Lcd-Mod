using System;
using System.Collections.Generic;
using System.Linq;
using LcdMod.Common.Config;
using LcdMod.Common.Helpers;
using LcdMod.Common.Networking;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Utils;
using VRageMath;

namespace LcdMod.Server
{
    public sealed class LcdModServerComponent
    {
        readonly LcdModSessionComponent _session;
        readonly Dictionary<long, IMyCubeGrid> _trackedGrids = new Dictionary<long, IMyCubeGrid>();
        readonly Dictionary<long, Dictionary<long, long>> _gridRemaps = new Dictionary<long, Dictionary<long, long>>();
        readonly Dictionary<string, HashSet<ulong>> _pendingTextureRequests = new Dictionary<string, HashSet<ulong>>(System.StringComparer.OrdinalIgnoreCase);
        readonly HashSet<IMyEntity> _entities = new HashSet<IMyEntity>();

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

            foreach (var grid in _trackedGrids.Values)
                UntrackGrid(grid);

            _trackedGrids.Clear();
            _gridRemaps.Clear();
            _pendingTextureRequests.Clear();
            _entities.Clear();
        }

        public void BeforeStart()
        {
            try
            {
                _entities.Clear();
                MyAPIGateway.Entities.GetEntities(_entities, entity => entity is IMyCubeGrid);

                foreach (var entity in _entities)
                    TrackGrid(entity as IMyCubeGrid);

                _entities.Clear();
            }
            catch (System.Exception e)
            {
                ErrorHandlerHelper.LogError(e, _session);
            }
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

                TrackGrid(grid);
            }
            catch (System.Exception e)
            {
                ErrorHandlerHelper.LogError(e, _session);
            }
        }

        void TrackGrid(IMyCubeGrid grid)
        {
            if (grid == null || grid.MarkedForClose || _trackedGrids.ContainsKey(grid.EntityId))
                return;

            _trackedGrids[grid.EntityId] = grid;
            grid.OnBlockAdded += BlockAdded;
            grid.OnMarkForClose += GridMarkedForClose;

            RemapHelper.RemapGrid(grid, GetRemap(grid));
        }

        void UntrackGrid(IMyCubeGrid grid)
        {
            if (grid == null)
                return;

            grid.OnBlockAdded -= BlockAdded;
            grid.OnMarkForClose -= GridMarkedForClose;
        }

        void GridMarkedForClose(IMyEntity entity)
        {
            try
            {
                var grid = entity as IMyCubeGrid;
                if (grid != null)
                    UntrackGrid(grid);

                _trackedGrids.Remove(entity.EntityId);
                _gridRemaps.Remove(entity.EntityId);
            }
            catch (System.Exception e)
            {
                ErrorHandlerHelper.LogError(e, _session);
            }
        }

        void BlockAdded(IMySlimBlock block)
        {
            try
            {
                var terminalBlock = block?.FatBlock as IMyTerminalBlock;
                if (terminalBlock == null)
                    return;

                RemapHelper.RemapGrid(terminalBlock.CubeGrid, GetRemap(terminalBlock.CubeGrid));
            }
            catch (System.Exception e)
            {
                ErrorHandlerHelper.LogError(e, _session);
            }
        }

        Dictionary<long, long> GetRemap(IMyCubeGrid grid)
        {
            Dictionary<long, long> remap;
            if (!_gridRemaps.TryGetValue(grid.EntityId, out remap))
            {
                remap = new Dictionary<long, long>();
                _gridRemaps[grid.EntityId] = remap;
            }

            return remap;
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

        public void HandleSortInventory(ReceivedPacketEventArgs args)
        {
            var packet = args.UnWrap<PacketSortInventory>();
            if (packet?.ContainerIds == null || packet.ContainerIds.Length < 2)
                return;

            var senderIdentity = MyAPIGateway.Players.TryGetIdentityId(args.SenderId);
            if (senderIdentity == 0)
                return;

            var blocks = new List<IMyTerminalBlock>(packet.ContainerIds.Length);
            for (var i = 0; i < packet.ContainerIds.Length; i++)
            {
                var block = MyEntities.GetEntityById(packet.ContainerIds[i]) as IMyTerminalBlock;
                if (block == null || !block.HasInventory)
                    continue;

                // Only honour containers the requester is actually allowed to use.
                if (block.GetUserRelationToOwner(senderIdentity) > MyRelationsBetweenPlayerAndBlock.FactionShare)
                    continue;

                blocks.Add(block);
            }

            InventorySorterCommon.Consolidate(blocks, (InventorySortMode)packet.Mode);
        }

        public void HandleTransferItems(ReceivedPacketEventArgs args)
        {
            var packet = args.UnWrap<PacketTransferItems>();
            if (packet == null || packet.TypeKeys == null || packet.TypeKeys.Length == 0)
                return;

            var senderIdentity = MyAPIGateway.Players.TryGetIdentityId(args.SenderId);
            if (senderIdentity == 0)
                return;

            var source = MyEntities.GetEntityById(packet.SourceId) as IMyTerminalBlock;
            if (source == null || !source.HasInventory ||
                source.GetUserRelationToOwner(senderIdentity) > MyRelationsBetweenPlayerAndBlock.FactionShare)
                return;

            var targets = new List<IMyTerminalBlock>(packet.TargetIds.Length);
            for (var i = 0; i < packet.TargetIds.Length; i++)
            {
                var block = MyEntities.GetEntityById(packet.TargetIds[i]) as IMyTerminalBlock;
                if (block == null || !block.HasInventory)
                    continue;

                if (block.GetUserRelationToOwner(senderIdentity) > MyRelationsBetweenPlayerAndBlock.FactionShare)
                    continue;

                targets.Add(block);
            }

            if (targets.Count == 0)
                return;

            var typeKeys = new HashSet<string>(packet.TypeKeys);
            InventoryDistributorCommon.Execute(source, targets, typeKeys, (TransferMode)packet.Mode);
        }

        public void HandleFillBlocks(ReceivedPacketEventArgs args)
        {
            var packet = args.UnWrap<PacketFillBlocks>();
            if (packet == null || packet.TargetIds == null || packet.TargetIds.Length == 0)
                return;

            var senderIdentity = MyAPIGateway.Players.TryGetIdentityId(args.SenderId);
            if (senderIdentity == 0)
                return;

            var targets = CollectOwnedBlocks(packet.TargetIds, senderIdentity);
            if (targets.Count == 0)
                return;

            var sources = CollectOwnedBlocks(packet.SourceIds, senderIdentity);
            BlockFillerCommon.Execute(sources, targets, (FillKind)packet.Kind);
        }

        // Resolves entity ids to inventory-bearing blocks the requester is actually allowed to use.
        static List<IMyTerminalBlock> CollectOwnedBlocks(long[] ids, long senderIdentity)
        {
            var blocks = new List<IMyTerminalBlock>(ids != null ? ids.Length : 0);
            if (ids == null)
                return blocks;

            for (var i = 0; i < ids.Length; i++)
            {
                var block = MyEntities.GetEntityById(ids[i]) as IMyTerminalBlock;
                if (block == null || !block.HasInventory)
                    continue;

                if (block.GetUserRelationToOwner(senderIdentity) > MyRelationsBetweenPlayerAndBlock.FactionShare)
                    continue;

                blocks.Add(block);
            }

            return blocks;
        }

        public void HandleRequestTexture(ReceivedPacketEventArgs args)
        {
            var packet = args.UnWrap<PacketRequestTexture>();
            if (packet == null || string.IsNullOrWhiteSpace(packet.TextureName))
                return;

            var requesterId = packet.RequesterSteamId != 0 ? packet.RequesterSteamId : args.SenderId;
            RequestTextureFromOwner(packet, requesterId);
        }

        public void HandleLocalRequestTexture(PacketRequestTexture packet)
        {
            if (packet == null || string.IsNullOrWhiteSpace(packet.TextureName))
                return;

            RequestTextureFromOwner(packet, packet.RequesterSteamId);
        }

        void RequestTextureFromOwner(PacketRequestTexture packet, ulong requesterId)
        {
            if (requesterId == 0)
                return;

            var cacheKey = TextureTransferHelper.BuildTextureKey(packet.OwnerSteamId, packet.TextureName);
            LogHelper.LogInfo($"Server requesting texture {cacheKey} for requester {requesterId}");

            byte[] textureBytes;
            if (TextureTransferHelper.TryLoadCachedTexture(packet.OwnerSteamId, packet.TextureName,
                    out textureBytes))
            {
                LogHelper.LogInfo($"Server found requested texture {cacheKey} in cache for requester {requesterId}");

                TextureTransferHelper.TextureMetadata metadata;
                if (!TextureTransferHelper.TryGetCachedTextureMetadata(packet.OwnerSteamId, packet.TextureName, out metadata))
                {
                    var ownerPlayer = FindPlayer(packet.OwnerSteamId);
                    metadata = new TextureTransferHelper.TextureMetadata
                    {
                        OwnerSteamId = packet.OwnerSteamId,
                        OwnerName = ownerPlayer != null ? ownerPlayer.DisplayName : packet.OwnerSteamId.ToString(),
                        RegistrationName = TextureTransferHelper.BuildTextureKey(packet.OwnerSteamId, packet.TextureName),
                        TextureName = TextureTransferHelper.NormalizeTextureName(packet.TextureName),
                        SourceFileName = TextureTransferHelper.BuildTextureFileName(packet.OwnerSteamId, packet.TextureName),
                        LastUpdatedUtcTicks = DateTime.UtcNow.Ticks
                    };
                }

                SendTextureToRequester(
                    new PacketSyncTexture(packet.OwnerSteamId, packet.TextureName, requesterId, textureBytes, metadata),
                    requesterId);
                return;
            }

            LogHelper.Log(MyLogSeverity.Warning,
                $"Server did not find requested texture {cacheKey} in cache; requesting from owner {packet.OwnerSteamId} for requester {requesterId}");

            if (string.IsNullOrEmpty(cacheKey))
                return;

            HashSet<ulong> pending;
            if (!_pendingTextureRequests.TryGetValue(cacheKey, out pending))
            {
                pending = new HashSet<ulong>();
                _pendingTextureRequests[cacheKey] = pending;
            }

            pending.Add(requesterId);

            if (pending.Count != 1)
                return;

            var owner = FindPlayer(packet.OwnerSteamId);
            if (owner == null)
            {
                LogHelper.Log(MyLogSeverity.Warning,
                    $"Server could not request texture {cacheKey}; owner {packet.OwnerSteamId} is not online");
                return;
            }

            var localSteamId = MyAPIGateway.Session?.Player?.SteamUserId ?? 0;
            if (localSteamId != 0 && owner.SteamUserId == localSteamId && LcdModSessionComponent.Client != null)
            {
                LogHelper.LogInfo($"Server forwarding texture request {cacheKey} to local host client for requester {requesterId}");
                LcdModSessionComponent.Client.HandleLocalRequestTexture(
                    new PacketRequestTexture(packet.OwnerSteamId, packet.TextureName, requesterId));
                return;
            }

            LogHelper.LogInfo($"Server forwarding texture request {cacheKey} to owner {owner.SteamUserId} for requester {requesterId}");

            LcdModSessionComponent.NetworkManager.TransmitToPlayer(
                new PacketRequestTexture(packet.OwnerSteamId, packet.TextureName, requesterId),
                owner.SteamUserId);
        }

        public void HandleSyncTexture(ReceivedPacketEventArgs args)
        {
            var packet = args.UnWrap<PacketSyncTexture>();
            if (packet == null || string.IsNullOrWhiteSpace(packet.TextureName) || !TextureTransferHelper.IsValidTexturePayload(packet.Data))
                return;

            if (args.SenderId != 0 && packet.OwnerSteamId != 0 && args.SenderId != packet.OwnerSteamId)
                return;

            SyncTextureFromOwner(packet);
        }

        public void HandleLocalSyncTexture(PacketSyncTexture packet)
        {
            if (packet == null || string.IsNullOrWhiteSpace(packet.TextureName) || !TextureTransferHelper.IsValidTexturePayload(packet.Data))
                return;

            SyncTextureFromOwner(packet);
        }

        void SyncTextureFromOwner(PacketSyncTexture packet)
        {
            var cacheKey = TextureTransferHelper.BuildTextureKey(packet.OwnerSteamId, packet.TextureName);
            LogHelper.LogInfo($"Server received requested texture {cacheKey} from owner {packet.OwnerSteamId} ({packet.Data.Length} bytes)");

            if (!TextureTransferHelper.TryCacheTexture(packet.OwnerSteamId, packet.TextureName, packet.Data, packet.Metadata))
            {
                LogHelper.Log(MyLogSeverity.Warning, $"Server failed to cache requested texture {cacheKey}");
                return;
            }

            HashSet<ulong> pending;
            if (!_pendingTextureRequests.TryGetValue(cacheKey, out pending))
                pending = null;

            if (pending == null || pending.Count == 0)
            {
                if (packet.RequesterSteamId != 0)
                {
                    SendTextureToRequester(
                        new PacketSyncTexture(packet.OwnerSteamId, packet.TextureName, packet.RequesterSteamId, packet.Data, packet.Metadata),
                        packet.RequesterSteamId);
                }

                return;
            }

            foreach (var requester in pending.ToList())
            {
                if (requester == 0)
                    continue;

                SendTextureToRequester(
                    new PacketSyncTexture(packet.OwnerSteamId, packet.TextureName, requester, packet.Data, packet.Metadata),
                    requester);
            }

            _pendingTextureRequests.Remove(cacheKey);
        }

        static void SendTextureToRequester(PacketSyncTexture packet, ulong requester)
        {
            var localSteamId = MyAPIGateway.Session?.Player?.SteamUserId ?? 0;
            if (localSteamId != 0 && requester == localSteamId && LcdModSessionComponent.Client != null)
            {
                LogHelper.LogInfo(
                    $"Server delivering requested texture {TextureTransferHelper.BuildTextureKey(packet.OwnerSteamId, packet.TextureName)} to local host client");
                LcdModSessionComponent.Client.HandleLocalSyncTexture(packet);
                return;
            }

            LcdModSessionComponent.NetworkManager.TransmitToPlayer(packet, requester);
        }

        static IMyPlayer FindPlayer(ulong steamUserId)
        {
            var players = new List<IMyPlayer>();
            MyAPIGateway.Players.GetPlayers(players);

            for (int i = 0; i < players.Count; i++)
            {
                var player = players[i];
                if (player != null && player.SteamUserId == steamUserId)
                    return player;
            }

            return null;
        }
    }
}
