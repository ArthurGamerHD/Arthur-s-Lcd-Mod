using System.Collections.Generic;
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
        readonly Dictionary<long, IMyCubeGrid> _trackedGrids = new Dictionary<long, IMyCubeGrid>();
        readonly Dictionary<long, Dictionary<long, long>> _gridRemaps = new Dictionary<long, Dictionary<long, long>>();
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
    }
}
