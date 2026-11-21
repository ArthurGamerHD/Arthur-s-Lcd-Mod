using System;
using System.Collections.Generic;
using System.Linq;
using Generated;
using LcdMod.Server.Audio;
using LcdMod.Common.Config;
using LcdMod.Common.Helpers;
using LcdMod.Common.Networking;
using LcdMod.Server.Images;
using LcdMod.Server.Modules.RoomEnvironment;
using LcdMod.Common.Images;
using LcdMod.Common.Snapshots;
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
    public partial class LcdModServerComponent : ISingleton<LcdModServerComponent>
    {
        readonly LcdModSessionComponent _session;
        readonly Dictionary<long, IMyCubeGrid> _trackedGrids = new Dictionary<long, IMyCubeGrid>();
        readonly Dictionary<long, Dictionary<long, long>> _gridRemaps = new Dictionary<long, Dictionary<long, long>>();
        readonly HashSet<IMyEntity> _entities = new HashSet<IMyEntity>();
        readonly NpcMarketService _npcMarket;
        readonly AudioBroadcastServerService _audioBroadcast;
        readonly GridImageServerService _images;
        readonly DroplineBridge _dropline = new DroplineBridge();

        public LcdModServerComponent(LcdModSessionComponent session)
        {
            RegisterSingleton();
            _session = session;
            _npcMarket = new NpcMarketService();
            RoomEnvironment = new GridRoomEnvironmentServerModule();
            _audioBroadcast = new AudioBroadcastServerService();
            _images = new GridImageServerService(() => _trackedGrids.Values);
        }

        public GridRoomEnvironmentServerModule RoomEnvironment { get; }

        public void LoadData()
        {
            MyAPIGateway.Entities.OnEntityAdd += EntityAdded;
            _dropline.LoadData();
        }

        public void UnloadData()
        {
            _audioBroadcast.Unload();
            _npcMarket.UnloadData();
            _dropline.UnloadData();
            RoomEnvironment.Unload();
            MyAPIGateway.Entities.OnEntityAdd -= EntityAdded;

            foreach (var grid in _trackedGrids.Values)
                UntrackGrid(grid);

            _trackedGrids.Clear();
            _gridRemaps.Clear();
            _images.Unload();
            GridSnapshotStorage.Clear();
            _entities.Clear();
            UnregisterSingleton();
        }

        public void BeforeStart()
        {
            try
            {
                _entities.Clear();
                MyAPIGateway.Entities.GetEntities(_entities, entity => entity is IMyCubeGrid);

                foreach (var entity in _entities)
                    TrackGrid(entity as IMyCubeGrid);

                foreach (var entity in _entities)
                    _images.EnsureReferencedImages(entity as IMyCubeGrid);

                _entities.Clear();
            }
            catch (Exception e)
            {
                ErrorHandlerHelper.LogError(e, _session);
            }
        }

        [NetworkCallback(typeof(NetworkPackageSyncComponentConfig), NetworkCallbackFilter.FromClient | NetworkCallbackFilter.IsServer)]
        public void HandleSyncConfig(ReceivedPacketEventArgs args)
        {
            var packet = args.UnWrap<NetworkPackageSyncComponentConfig>();
            if (packet?.Config == null)
                return;
            // Cockpits and other screen providers are terminal blocks but not functional blocks.
            var block = MyEntities.GetEntityById(packet.BlockId) as IMyTerminalBlock;
            if (block == null)
                return;
            long identity = MyAPIGateway.Players.TryGetIdentityId(args.SenderId);
            if (MyAPIGateway.Multiplayer.MultiplayerActive &&
                (identity == 0 || block.GetUserRelationToOwner(identity) > MyRelationsBetweenPlayerAndBlock.FactionShare))
            {
                LogHelper.Log(MyLogSeverity.Warning, "Rejected screen config for block " + block.EntityId + " from " +
                                                     args.SenderId + ": not owner or faction share");
                return;
            }

            RemapHelper.PinBlocks(packet.Config);
            ScreenProviderConfigStorage.Save(block, packet.Config);
            _images.EnsureConfigImages(block.CubeGrid, packet.Config);
        }

        [NetworkCallback(typeof(PacketStoreGridSnapshot), NetworkCallbackFilter.FromClient | NetworkCallbackFilter.IsServer)]
        public void HandleStoreGridSnapshot(ReceivedPacketEventArgs args)
        {
            StoreGridSnapshot(args.SenderId, args.UnWrap<PacketStoreGridSnapshot>());
        }

        public void HandleLocalStoreGridSnapshot(PacketStoreGridSnapshot packet)
        {
            var player = MyAPIGateway.Session.Player;
            StoreGridSnapshot(player?.SteamUserId ?? MyAPIGateway.Multiplayer.MyId, packet);
        }

        // Kept on the screen's block (saved with the world) and sent to every client, whose screens draw from it.
        void StoreGridSnapshot(ulong senderSteamId, PacketStoreGridSnapshot packet)
        {
            if (packet == null || string.IsNullOrEmpty(packet.Key) || packet.Key.Length > 64)
                return;
            byte[] data = packet.Data ?? Array.Empty<byte>();
            if (data.Length > LcdMod.Common.Helpers.Constants.MAX_GRID_SNAPSHOT_BYTES)
                return;
            var block = MyEntities.GetEntityById(packet.BlockId) as IMyTerminalBlock;
            if (block == null)
                return;
            long identity = MyAPIGateway.Players.TryGetIdentityId(senderSteamId);
            if (MyAPIGateway.Multiplayer.MultiplayerActive &&
                (identity == 0 || block.GetUserRelationToOwner(identity) > MyRelationsBetweenPlayerAndBlock.FactionShare))
                return;
            if (GridSnapshotStorage.TotalBytes(block, packet.Key) + data.Length > LcdMod.Common.Helpers.Constants.MAX_BLOCK_SNAPSHOT_BYTES)
            {
                LogHelper.Log(MyLogSeverity.Warning, "Rejected grid snapshot " + packet.Key + " from " + senderSteamId +
                                                     ": block " + block.EntityId + " is at its snapshot storage limit");
                return;
            }
            GridSnapshotStorage.Set(block, packet.Key, data);

            var sync = new PacketSyncGridSnapshot { BlockId = packet.BlockId, Key = packet.Key, Data = data };
            var players = new List<IMyPlayer>();
            MyAPIGateway.Players.GetPlayers(players);
            ulong local = MyAPIGateway.Session.Player?.SteamUserId ?? 0;
            for (int i = 0; i < players.Count; i++)
            {
                IMyPlayer player = players[i];
                // The sender already holds it; so does this server's own player.
                if (player == null || player.IsBot || player.SteamUserId == 0 || player.SteamUserId == local ||
                    player.SteamUserId == senderSteamId)
                    continue;
                LcdModSessionComponent.NetworkManager.TransmitToPlayer(sync, player.SteamUserId);
            }
        }

        public void SaveData()
        {
            _npcMarket.SaveData();
            _dropline.SaveData();
            _images.Prune();
        }

        
        [NetworkCallback(typeof(PacketRequestNpcMarket), NetworkCallbackFilter.FromClient | NetworkCallbackFilter.IsServer)]
        public void HandleRequestNpcMarket(ReceivedPacketEventArgs args)
        {
            _npcMarket.HandleRequest(args.SenderId, args.UnWrap<PacketRequestNpcMarket>());
        }

        public void HandleLocalRequestNpcMarket(PacketRequestNpcMarket packet)
        {
            var player = MyAPIGateway.Session.Player;
            _npcMarket.HandleRequest(player?.SteamUserId ?? MyAPIGateway.Multiplayer.MyId, packet);
        }

        [NetworkCallback(typeof(PacketDroplineRequest), NetworkCallbackFilter.FromClient | NetworkCallbackFilter.IsServer)]
        public void HandleDroplineRequest(ReceivedPacketEventArgs args)
        {
            _dropline.HandleRequest(args.SenderId, args.UnWrap<PacketDroplineRequest>());
        }

        public void HandleLocalDroplineRequest(PacketDroplineRequest packet)
        {
            var player = MyAPIGateway.Session.Player;
            _dropline.HandleRequest(player?.SteamUserId ?? MyAPIGateway.Multiplayer.MyId, packet);
        }

        [NetworkCallback(typeof(PacketRequestBroadcastAudio), NetworkCallbackFilter.FromClient | NetworkCallbackFilter.IsServer)]
        public void HandleRequestBroadcastAudio(ReceivedPacketEventArgs args)
        {
            _audioBroadcast.HandleRequest(args.SenderId, args.UnWrap<PacketRequestBroadcastAudio>());
        }

        public void HandleLocalRequestBroadcastAudio(PacketRequestBroadcastAudio packet)
        {
            var player = MyAPIGateway.Session.Player;
            _audioBroadcast.HandleRequest(player?.SteamUserId ?? MyAPIGateway.Multiplayer.MyId, packet);
        }

        
        [NetworkCallback(typeof(PacketRequestMediaPlayerCommand), NetworkCallbackFilter.FromClient | NetworkCallbackFilter.IsServer)]
        public void HandleRequestMediaPlayerCommand(ReceivedPacketEventArgs args)
        {
            _audioBroadcast.HandleMediaPlayerCommandRequest(args.SenderId, args.UnWrap<PacketRequestMediaPlayerCommand>());
        }

        public void HandleLocalRequestMediaPlayerCommand(PacketRequestMediaPlayerCommand packet)
        {
            var player = MyAPIGateway.Session.Player;
            _audioBroadcast.HandleMediaPlayerCommandRequest(player?.SteamUserId ?? MyAPIGateway.Multiplayer.MyId, packet);
        }

        [NetworkCallback(typeof(PacketRequestMediaStreamChunk), NetworkCallbackFilter.FromClient | NetworkCallbackFilter.IsServer)]
        public void HandleRequestMediaStreamChunk(ReceivedPacketEventArgs args)
        {
            _audioBroadcast.HandleMediaStreamChunkRequest(args.SenderId, args.UnWrap<PacketRequestMediaStreamChunk>());
        }

        public void HandleLocalRequestMediaStreamChunk(PacketRequestMediaStreamChunk packet)
        {
            var player = MyAPIGateway.Session.Player;
            _audioBroadcast.HandleMediaStreamChunkRequest(player?.SteamUserId ?? MyAPIGateway.Multiplayer.MyId, packet);
        }

          
        [NetworkCallback(typeof(PacketMediaStreamControl), NetworkCallbackFilter.FromClient | NetworkCallbackFilter.IsServer)]
        public void HandleMediaStreamControl(ReceivedPacketEventArgs args)
        {
            _audioBroadcast.HandleMediaStreamControl(args.SenderId, args.UnWrap<PacketMediaStreamControl>());
        }

        public void HandleLocalMediaStreamControl(PacketMediaStreamControl packet)
        {
            var player = MyAPIGateway.Session.Player;
            _audioBroadcast.HandleMediaStreamControl(player?.SteamUserId ?? MyAPIGateway.Multiplayer.MyId, packet);
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
            catch (Exception e)
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
            grid.OnGridSplit += GridSplit;
            grid.OnGridMerge += GridMerged;

            RemapHelper.RemapGrid(grid, GetRemap(grid));
        }

        void UntrackGrid(IMyCubeGrid grid)
        {
            if (grid == null)
                return;

            grid.OnBlockAdded -= BlockAdded;
            grid.OnMarkForClose -= GridMarkedForClose;
            grid.OnGridSplit -= GridSplit;
            grid.OnGridMerge -= GridMerged;
        }

        void GridSplit(IMyCubeGrid original, IMyCubeGrid split)
        {
            try
            {
                TrackGrid(split);
                _images.HandleGridSplit(original, split);
            }
            catch (Exception e)
            {
                ErrorHandlerHelper.LogError(e, _session);
            }
        }

        void GridMerged(IMyCubeGrid kept, IMyCubeGrid removed)
        {
            try
            {
                _images.HandleGridMerge(kept, removed);
            }
            catch (Exception e)
            {
                ErrorHandlerHelper.LogError(e, _session);
            }
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
                GridImageStorage.Forget(entity.EntityId);
            }
            catch (Exception e)
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
            catch (Exception e)
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

        [NetworkCallback(typeof(PacketEditFaction), NetworkCallbackFilter.FromClient | NetworkCallbackFilter.IsServer)]
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

        [NetworkCallback(typeof(PacketSortInventory), NetworkCallbackFilter.FromClient | NetworkCallbackFilter.IsServer)]
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

        [NetworkCallback(typeof(PacketTransferItems), NetworkCallbackFilter.FromClient | NetworkCallbackFilter.IsServer)]
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
        
        [NetworkCallback(typeof(PacketFillBlocks), NetworkCallbackFilter.FromClient | NetworkCallbackFilter.IsServer)]
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
            BlockFillerCommon.Execute(sources, targets, (FillKind)packet.Kind, packet.Settings);
        }

        static List<IMyTerminalBlock> CollectOwnedBlocks(long[] ids, long senderIdentity)
        {
            var blocks = new List<IMyTerminalBlock>(ids?.Length ?? 0);
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

        [NetworkCallback(typeof(PacketRequestGridRoomEnvironment), NetworkCallbackFilter.FromClient | NetworkCallbackFilter.IsServer)]
        internal void HandleRequestGridRoomEnvironment(ReceivedPacketEventArgs args) =>
            Instance.RoomEnvironment.HandleRequestGridRoomEnvironment(args);
        
        [NetworkCallback(typeof(PacketUploadGridImage), NetworkCallbackFilter.FromClient | NetworkCallbackFilter.IsServer)]
        public void HandleUploadGridImage(ReceivedPacketEventArgs args)
        {
            _images.HandleUpload(args.SenderId, args.UnWrap<PacketUploadGridImage>());
        }

        public void HandleLocalUploadGridImage(PacketUploadGridImage packet)
        {
            var player = MyAPIGateway.Session.Player;
            _images.HandleUpload(player?.SteamUserId ?? MyAPIGateway.Multiplayer.MyId, packet);
        }

        [NetworkCallback(typeof(PacketRequestGridImage), NetworkCallbackFilter.FromClient | NetworkCallbackFilter.IsServer)]
        public void HandleRequestGridImage(ReceivedPacketEventArgs args)
        {
            var requester = args.SenderId;
            _images.HandleRequest(requester, args.UnWrap<PacketRequestGridImage>(),
                reply => LcdModSessionComponent.NetworkManager.TransmitToPlayer(reply, requester));
        }

        public void HandleLocalRequestGridImage(PacketRequestGridImage packet)
        {
            var player = MyAPIGateway.Session.Player;
            _images.HandleRequest(player?.SteamUserId ?? MyAPIGateway.Multiplayer.MyId, packet,
                reply => LcdModSessionComponent.Client?.HandleLocalGridImageData(reply));
        }
    }
}
