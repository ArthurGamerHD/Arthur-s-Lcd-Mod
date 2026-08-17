using System;
using System.Collections.Generic;
using System.Linq;
#if EXPERIMENTAL
using LcdMod.Client.Diagnostics;
#endif
using LcdMod.Common.Helpers;
using LcdMod.Common.Mvvm;
using LcdMod.Common.Networking;
using Sandbox.ModAPI;
using VRage;
using VRage.Game.Entity;
using IMyCubeBlock = VRage.Game.ModAPI.IMyCubeBlock;
using IMyCubeGrid = VRage.Game.ModAPI.IMyCubeGrid;
using IMyGridGroupData = VRage.Game.ModAPI.IMyGridGroupData;
using IMySlimBlock = VRage.Game.ModAPI.IMySlimBlock;
using IMyTerminalBlock = Sandbox.ModAPI.IMyTerminalBlock;
using IMyGridTerminalSystem = Sandbox.ModAPI.IMyGridTerminalSystem;
using IMyBlockGroup = Sandbox.ModAPI.IMyBlockGroup;
using IMyFarmPlotLogic = Sandbox.ModAPI.IMyFarmPlotLogic;
using IMyFunctionalBlock = Sandbox.ModAPI.IMyFunctionalBlock;
using IMyResourceStorageComponent = Sandbox.ModAPI.IMyResourceStorageComponent;
using GridLinkTypeEnum = VRage.Game.ModAPI.GridLinkTypeEnum;
using MyItemType = VRage.Game.ModAPI.Ingame.MyItemType;

namespace LcdMod.Client.GridData
{
    [Flags]
    public enum GridCapability
    {
        None = 0,
        Blocks = 1,
        Items = 2,
        InventoryTopology = 4
    }

    /// <summary>
    ///     Event-driven logic attached to a grid.
    /// </summary>
    public class GridLogic
    {
        const int INVENTORY_RECONCILIATION_INTERVAL_FRAMES = 600;
        bool _isUnloaded;
        bool _gridBlockEventsBound;
        bool _inventoryTopologyRefreshScheduled;
        long _nextInventoryReconciliationFrame;
        int _blockNeedCount;
        int _itemNeedCount;
        int _inventoryTopologyNeedCount;
        IMyGridGroupData _parentGroup;
        IMyGridGroupData _terminalParentGroup;
        IMyGridTerminalSystem _terminalSystem;
        readonly Dictionary<GridLinkTypeEnum, IMyGridGroupData> _watchedLinkGroups =
            new Dictionary<GridLinkTypeEnum, IMyGridGroupData>();
        readonly Dictionary<IMyCubeBlock, List<MyInventoryBase>> _inventoriesByBlock =
            new Dictionary<IMyCubeBlock, List<MyInventoryBase>>();
        readonly Dictionary<IMyCubeBlock, FarmPlotEntry> _farmPlotsByBlock =
            new Dictionary<IMyCubeBlock, FarmPlotEntry>();
        GridRoomEnvironmentComponent _roomEnvironmentComponent;

        public readonly IMyCubeGrid Grid;
        TypedBlockCollection _blocks;
        TypedItemCollection _items;
        ObservableList<FarmPlotEntry> _farmPlots;
        public readonly MediaPlayerRegistry MediaPlayers = new MediaPlayerRegistry();

        public event GridGroupChanged GroupChanged;
        public event GridLinkChanged LinkChanged;
        public event TerminalGroupChanged TerminalGroupChanged;
        public event InventoryTopologyChanged InventoryTopologyChanged;

        public GridLogic(IMyCubeGrid grid)
        {
            Grid = grid;

            if (Grid == null)
                return;

            BindParentGroup(GetParentGroup());
            BindTerminalParentGroup(GetTerminalParentGroup());
            BindTerminalSystem(GetTerminalSystem());
        }

        public long TargetGrid => Grid?.EntityId ?? 0L;

        public bool IsAlive => !_isUnloaded && Grid != null && !Grid.Closed && !Grid.MarkedForClose;

        public bool HasBlockNeed => _blockNeedCount > 0;
        public bool HasItemNeed => _itemNeedCount > 0;
        public bool HasInventoryTopologyNeed => _inventoryTopologyNeedCount > 0;
        public int BlockNeedCount => _blockNeedCount;
        public int ItemNeedCount => _itemNeedCount;
        public int InventoryTopologyNeedCount => _inventoryTopologyNeedCount;
        public int TrackedBlockCount => _blocks?.Count ?? 0;
        public int TrackedItemCount => _items?.Count ?? 0;

        public TypedBlockCollection Blocks
        {
            get
            {
                if (_blocks == null)
                    throw new InvalidOperationException("Blocks were accessed without an active GridDataNeed.Blocks request.");
                return _blocks;
            }
        }

        public TypedItemCollection Items
        {
            get
            {
                if (_items == null)
                    throw new InvalidOperationException("Items were accessed without an active GridDataNeed.Items request.");
                return _items;
            }
        }

        public ObservableList<FarmPlotEntry> FarmPlots
        {
            get
            {
                if (_farmPlots == null)
                    throw new InvalidOperationException("Farm plots were accessed without an active GridDataNeed.Blocks request.");
                return _farmPlots;
            }
        }

        private GridRoomEnvironmentComponent RoomEnvironment =>
            _roomEnvironmentComponent ?? (_roomEnvironmentComponent = new GridRoomEnvironmentComponent(this));

        internal bool TryGetGridRoomEnvironment(IMyCubeBlock block, out GridRoomEnvironmentSample sample)
        {
            return RoomEnvironment.TryGetGridRoomEnvironment(block, out sample);
        }

        internal void ApplyGridRoomEnvironment(PacketSyncGridRoomEnvironment packet)
        {
            RoomEnvironment.ApplyGridRoomEnvironment(packet);
        }

        public void Update()
        {
            if (!IsAlive || MyAPIGateway.Session == null)
                return;

            MediaPlayers.Update();

            if (_items == null)
                return;

            var currentFrame = MyAPIGateway.Session.GameplayFrameCounter;
            if (currentFrame < _nextInventoryReconciliationFrame)
                return;

            _nextInventoryReconciliationFrame = currentFrame + INVENTORY_RECONCILIATION_INTERVAL_FRAMES;
            _items.ReconcileNextInventory();
        }

        public void RequestCapability(GridCapability capability)
        {
            if (_isUnloaded)
                throw new InvalidOperationException("Cannot request data from an unloaded GridLogic.");

            if ((capability & GridCapability.Blocks) != 0)
            {
                _blockNeedCount++;
                if (_blockNeedCount == 1)
                    StartBlockModuleIfNeeded();
            }

            if ((capability & GridCapability.Items) != 0)
            {
                _itemNeedCount++;
                if (_itemNeedCount == 1)
                    StartItemModuleIfNeeded();
            }

            if ((capability & GridCapability.InventoryTopology) != 0)
            {
                _inventoryTopologyNeedCount++;
                if (_inventoryTopologyNeedCount == 1)
                    BindGridBlockEvents();
            }
        }

        public void Release(GridCapability need)
        {
            if ((need & GridCapability.InventoryTopology) != 0 && _inventoryTopologyNeedCount > 0)
            {
                _inventoryTopologyNeedCount--;
                if (_inventoryTopologyNeedCount == 0)
                    UnbindGridBlockEventsIfUnused();
            }

            if ((need & GridCapability.Items) != 0 && _itemNeedCount > 0)
            {
                _itemNeedCount--;
                if (_itemNeedCount == 0)
                    ScheduleItemModuleStop();
            }

            if ((need & GridCapability.Blocks) != 0 && _blockNeedCount > 0)
            {
                _blockNeedCount--;
                if (_blockNeedCount == 0)
                    ScheduleBlockModuleStop();
            }
        }

        void ScheduleBlockModuleStop()
        {
            if (HasBlockNeed)
                return;

            LcdModClientComponent.RunNextFrame.Add(delegate
            {
                if (HasBlockNeed)
                    return;

                StopBlockModule();
            });
        }

        void ScheduleItemModuleStop()
        {
            if (HasItemNeed)
                return;

            LcdModClientComponent.RunNextFrame.Add(delegate
            {
                if (HasItemNeed)
                    return;

                StopItemModule();
            });
        }

        void StartBlockModuleIfNeeded()
        {
            if(_blocks != null)
                return;

            _blocks = new TypedBlockCollection();
            _farmPlots = new ObservableList<FarmPlotEntry>();
            BindGridBlockEvents();
            RefreshInventoryTopologyCore();
        }

        void StopBlockModule()
        {
            _farmPlotsByBlock.Clear();
            _farmPlots?.Clear();
            _blocks?.Clear();
            _farmPlots = null;
            _blocks = null;
            UnbindGridBlockEventsIfUnused();
        }

        void StartItemModuleIfNeeded()
        {
            if(_items != null)
                return;

            _items = new TypedItemCollection();
            var currentFrame = MyAPIGateway.Session != null
                ? MyAPIGateway.Session.GameplayFrameCounter
                : 0L;
            var reconciliationOffset = (int)((TargetGrid & long.MaxValue) %
                                             INVENTORY_RECONCILIATION_INTERVAL_FRAMES);
            _nextInventoryReconciliationFrame = currentFrame + reconciliationOffset;
            BindGridBlockEvents();
            RefreshInventoryTopologyCore();
        }

        void StopItemModule()
        {
            foreach (var inventory in _inventoriesByBlock.Values.SelectMany(inventories => inventories))
            {
                inventory.InventoryContentChanged -= OnInventoryContentChanged;
                inventory.ContentsChanged -= OnInventoryContentsChanged;
            }

            _inventoriesByBlock.Clear();
            if (_items != null)
                _items.Clear();
            _items = null;
            _inventoryTopologyRefreshScheduled = false;
            UnbindGridBlockEventsIfUnused();
        }

        void BindGridBlockEvents()
        {
            if (_gridBlockEventsBound || Grid == null)
                return;

            Grid.OnBlockAdded += OnBlockAdded;
            Grid.OnBlockRemoved += OnBlockRemoved;
            _gridBlockEventsBound = true;
        }

        void UnbindGridBlockEventsIfUnused()
        {
            if (!_gridBlockEventsBound || _blocks != null || _items != null || HasInventoryTopologyNeed || Grid == null)
                return;

            Grid.OnBlockAdded -= OnBlockAdded;
            Grid.OnBlockRemoved -= OnBlockRemoved;
            _gridBlockEventsBound = false;
        }

        public void Unload()
        {
            if (_isUnloaded)
                return;

            _isUnloaded = true;

            if (Grid != null && _gridBlockEventsBound)
            {
                Grid.OnBlockAdded -= OnBlockAdded;
                Grid.OnBlockRemoved -= OnBlockRemoved;
                _gridBlockEventsBound = false;
            }

            BindParentGroup(null);
            UnbindWatchedLinkGroups();
            BindTerminalParentGroup(null);
            BindTerminalSystem(null);

            StopItemModule();
            StopBlockModule();
            _blockNeedCount = 0;
            _itemNeedCount = 0;
            MediaPlayers.Unload();
        }

        IMyGridGroupData GetParentGroup()
        {
            return MyAPIGateway.GridGroups != null && Grid != null
                ? MyAPIGateway.GridGroups.GetGridGroup(GridLinkTypeEnum.Mechanical, Grid)
                : null;
        }

        IMyGridGroupData GetTerminalParentGroup()
        {
            return MyAPIGateway.GridGroups != null && Grid != null
                ? MyAPIGateway.GridGroups.GetGridGroup(GridLinkTypeEnum.Logical, Grid)
                : null;
        }

        IMyGridTerminalSystem GetTerminalSystem()
        {
            return MyAPIGateway.TerminalActionsHelper != null && Grid != null
                ? MyAPIGateway.TerminalActionsHelper.GetTerminalSystemForGrid(Grid)
                : null;
        }

        void BindParentGroup(IMyGridGroupData group)
        {
            if (ReferenceEquals(_parentGroup, group))
                return;

            if (_parentGroup != null)
            {
                _parentGroup.OnGridAdded -= OnParentGridAdded;
                _parentGroup.OnGridRemoved -= OnParentGridRemoved;
                _parentGroup.OnReleased -= OnParentGroupReleased;
            }

            _parentGroup = group;
            if (_parentGroup != null)
            {
                _parentGroup.OnGridAdded += OnParentGridAdded;
                _parentGroup.OnGridRemoved += OnParentGridRemoved;
                _parentGroup.OnReleased += OnParentGroupReleased;
            }
        }

        void OnParentGridAdded(IMyGridGroupData group, IMyCubeGrid grid, IMyGridGroupData previousGroup)
        {
            if (ReferenceEquals(group, _parentGroup))
                RaiseGroupChanged();
        }

        void OnParentGridRemoved(IMyGridGroupData group, IMyCubeGrid grid, IMyGridGroupData nextGroup)
        {
            if (!ReferenceEquals(group, _parentGroup))
                return;

            if (grid != null && Grid != null && grid.EntityId == Grid.EntityId)
                BindParentGroup(nextGroup ?? GetParentGroup());

            RaiseGroupChanged();
        }

        void OnParentGroupReleased(IMyGridGroupData group)
        {
            if (!ReferenceEquals(group, _parentGroup))
                return;

            BindParentGroup(null);
            var nextGroup = GetParentGroup();
            if (!ReferenceEquals(nextGroup, group))
                BindParentGroup(nextGroup);

            RaiseGroupChanged();
        }

        void RaiseGroupChanged()
        {
            try
            {
                var handler = GroupChanged;
                if (handler != null)
                    handler(this, new GridGroupChangedArgs());
            }
            finally
            {
                RaiseLinkChanged(GridLinkTypeEnum.Mechanical);
            }
        }

        public void WatchLinkType(GridLinkTypeEnum linkType)
        {
            if (linkType == GridLinkTypeEnum.Mechanical)
                return;

            var current = GetLinkGroup(linkType);
            IMyGridGroupData watched;
            if (!_watchedLinkGroups.TryGetValue(linkType, out watched) || !ReferenceEquals(watched, current))
                BindWatchedLinkGroup(linkType, current);
        }

        public void GetLinkedGrids(GridLinkTypeEnum linkType, ICollection<IMyCubeGrid> grids)
        {
            if (grids == null)
                return;

            grids.Clear();
            if (MyAPIGateway.GridGroups == null || Grid == null)
                return;

            MyAPIGateway.GridGroups.GetGroup(Grid, linkType, grids);
        }

        IMyGridGroupData GetLinkGroup(GridLinkTypeEnum linkType)
        {
            return MyAPIGateway.GridGroups != null && Grid != null
                ? MyAPIGateway.GridGroups.GetGridGroup(linkType, Grid)
                : null;
        }

        void BindWatchedLinkGroup(GridLinkTypeEnum linkType, IMyGridGroupData group)
        {
            IMyGridGroupData previous;
            if (_watchedLinkGroups.TryGetValue(linkType, out previous))
            {
                if (ReferenceEquals(previous, group))
                    return;

                if (previous != null)
                {
                    previous.OnGridAdded -= OnWatchedLinkGridAdded;
                    previous.OnGridRemoved -= OnWatchedLinkGridRemoved;
                    previous.OnReleased -= OnWatchedLinkGroupReleased;
                }
            }

            _watchedLinkGroups[linkType] = group;
            if (group != null)
            {
                group.OnGridAdded += OnWatchedLinkGridAdded;
                group.OnGridRemoved += OnWatchedLinkGridRemoved;
                group.OnReleased += OnWatchedLinkGroupReleased;
            }
        }

        void UnbindWatchedLinkGroups()
        {
            foreach (var group in _watchedLinkGroups.Values)
            {
                if (group == null)
                    continue;

                group.OnGridAdded -= OnWatchedLinkGridAdded;
                group.OnGridRemoved -= OnWatchedLinkGridRemoved;
                group.OnReleased -= OnWatchedLinkGroupReleased;
            }

            _watchedLinkGroups.Clear();
        }

        void OnWatchedLinkGridAdded(IMyGridGroupData group, IMyCubeGrid grid, IMyGridGroupData previousGroup)
        {
            GridLinkTypeEnum linkType;
            if (TryGetWatchedLinkType(group, out linkType))
                RaiseLinkChanged(linkType);
        }

        void OnWatchedLinkGridRemoved(IMyGridGroupData group, IMyCubeGrid grid, IMyGridGroupData nextGroup)
        {
            GridLinkTypeEnum linkType;
            if (!TryGetWatchedLinkType(group, out linkType))
                return;

            if (grid != null && Grid != null && grid.EntityId == Grid.EntityId)
                BindWatchedLinkGroup(linkType, nextGroup ?? GetLinkGroup(linkType));

            RaiseLinkChanged(linkType);
        }

        void OnWatchedLinkGroupReleased(IMyGridGroupData group)
        {
            GridLinkTypeEnum linkType;
            if (!TryGetWatchedLinkType(group, out linkType))
                return;

            BindWatchedLinkGroup(linkType, null);
            var nextGroup = GetLinkGroup(linkType);
            if (!ReferenceEquals(nextGroup, group))
                BindWatchedLinkGroup(linkType, nextGroup);
            RaiseLinkChanged(linkType);
        }

        bool TryGetWatchedLinkType(IMyGridGroupData group, out GridLinkTypeEnum linkType)
        {
            foreach (var watched in _watchedLinkGroups)
            {
                if (ReferenceEquals(watched.Value, group))
                {
                    linkType = watched.Key;
                    return true;
                }
            }

            linkType = default(GridLinkTypeEnum);
            return false;
        }

        void RaiseLinkChanged(GridLinkTypeEnum linkType)
        {
#if EXPERIMENTAL
            using (RuntimeProfiler.Measure(
                       "event.grid",
                       "link_changed." + linkType,
                       null,
                       TargetGrid))
#endif
            {
                try
                {
                    var handler = LinkChanged;
                    if (handler != null)
                        handler(this, new GridLinkChangedArgs(linkType));
                }
                finally
                {
                    ScheduleInventoryTopologyRefresh();
                }
            }
        }

        void BindTerminalParentGroup(IMyGridGroupData group)
        {
            if (ReferenceEquals(_terminalParentGroup, group))
                return;

            if (_terminalParentGroup != null)
            {
                _terminalParentGroup.OnGridAdded -= OnTerminalParentGridAdded;
                _terminalParentGroup.OnGridRemoved -= OnTerminalParentGridRemoved;
                _terminalParentGroup.OnReleased -= OnTerminalParentGroupReleased;
            }

            _terminalParentGroup = group;
            if (_terminalParentGroup != null)
            {
                _terminalParentGroup.OnGridAdded += OnTerminalParentGridAdded;
                _terminalParentGroup.OnGridRemoved += OnTerminalParentGridRemoved;
                _terminalParentGroup.OnReleased += OnTerminalParentGroupReleased;
            }
        }

        void BindTerminalSystem(IMyGridTerminalSystem terminalSystem)
        {
            if (ReferenceEquals(_terminalSystem, terminalSystem))
                return;

            if (_terminalSystem != null)
            {
                _terminalSystem.GroupAdded -= OnTerminalGroupAdded;
                _terminalSystem.GroupRemoved -= OnTerminalGroupRemoved;
            }

            _terminalSystem = terminalSystem;
            if (_terminalSystem != null)
            {
                _terminalSystem.GroupAdded += OnTerminalGroupAdded;
                _terminalSystem.GroupRemoved += OnTerminalGroupRemoved;
            }
        }

        void OnTerminalParentGridAdded(IMyGridGroupData group, IMyCubeGrid grid, IMyGridGroupData previousGroup)
        {
            RefreshTerminalSystem();
        }

        void OnTerminalParentGridRemoved(IMyGridGroupData group, IMyCubeGrid grid, IMyGridGroupData nextGroup)
        {
            if (grid != null && Grid != null && grid.EntityId == Grid.EntityId)
                BindTerminalParentGroup(nextGroup ?? GetTerminalParentGroup());

            RefreshTerminalSystem();
        }

        void OnTerminalParentGroupReleased(IMyGridGroupData group)
        {
            if (!ReferenceEquals(group, _terminalParentGroup))
                return;

            BindTerminalParentGroup(null);
            BindTerminalParentGroup(GetTerminalParentGroup());
            RefreshTerminalSystem();
        }

        void RefreshTerminalSystem()
        {
            BindTerminalSystem(GetTerminalSystem());
            try
            {
                RaiseTerminalGroupChanged(null);
            }
            finally
            {
                ScheduleInventoryTopologyRefresh();
            }
        }

        void OnTerminalGroupAdded(IMyBlockGroup group)
        {
            RaiseTerminalGroupChanged(group?.Name);
        }

        void OnTerminalGroupRemoved(IMyBlockGroup group) => RaiseTerminalGroupChanged(group?.Name);

        void RaiseTerminalGroupChanged(string groupName)
        {
            var handler = TerminalGroupChanged;
            if (handler != null)
                handler(this, new TerminalGroupChangedArgs(groupName));
        }

        public void GetTerminalGroupBlocks(IEnumerable<string> groupNames, List<IMyTerminalBlock> blocks)
        {
            if (blocks == null)
                return;

            blocks.Clear();
            var terminalSystem = _terminalSystem ?? GetTerminalSystem();
            if (terminalSystem == null || groupNames == null)
                return;

            var seen = new HashSet<long>();
            var groupBlocks = new List<IMyTerminalBlock>();
            foreach (var groupName in groupNames)
            {
                if (string.IsNullOrEmpty(groupName))
                    continue;

                var group = terminalSystem.GetBlockGroupWithName(groupName);
                if (group == null)
                    continue;

                groupBlocks.Clear();
                group.GetBlocks(groupBlocks);
                foreach (var block in groupBlocks)
                {
                    if (block != null && seen.Add(block.EntityId))
                        blocks.Add(block);
                }
            }
        }

        void OnBlockAdded(IMySlimBlock block)
        {
#if EXPERIMENTAL
            using (RuntimeProfiler.Measure("event.grid", "block_added", null, TargetGrid))
#endif
            {
                try
                {
                    if (block.FatBlock != null)
                    {
                        var fatBlock = block.FatBlock;
                        if (_blocks != null && !_blocks.All.Contains(fatBlock))
                            _blocks.Add(fatBlock);
                        if (_blocks != null)
                            AddFarmPlot(fatBlock);
                        if (_items != null)
                            RefreshBlockInventories(fatBlock);
                    }
                }
                finally
                {
                    if (_items != null || HasInventoryTopologyNeed)
                        ScheduleInventoryTopologyRefresh();
                }
            }
        }

        void OnBlockRemoved(IMySlimBlock block)
        {
#if EXPERIMENTAL
            using (RuntimeProfiler.Measure("event.grid", "block_removed", null, TargetGrid))
#endif
            {
                try
                {
                    if (block.FatBlock == null)
                        return;

                    var fatBlock = block.FatBlock;
                    if (_items != null)
                        RemoveBlockInventories(fatBlock);
                    if (_blocks != null)
                    {
                        RemoveFarmPlot(fatBlock);
                        _blocks.Remove(fatBlock);
                    }
                }
                finally
                {
                    if (_items != null || HasInventoryTopologyNeed)
                        ScheduleInventoryTopologyRefresh();
                }
            }
        }

        void RefreshBlockInventories(IMyCubeBlock block)
        {
            if (block == null)
                return;

            var currentInventories = new List<MyInventoryBase>();
            if (block.HasInventory)
            {
                for (var inventoryIndex = 0; inventoryIndex < block.InventoryCount; inventoryIndex++)
                {
                    var inventory = block.GetInventory(inventoryIndex) as MyInventoryBase;
                    if (inventory != null && !currentInventories.Contains(inventory))
                        currentInventories.Add(inventory);
                }
            }

            List<MyInventoryBase> trackedInventories;
            if (!_inventoriesByBlock.TryGetValue(block, out trackedInventories))
            {
                trackedInventories = new List<MyInventoryBase>();
                if (currentInventories.Count > 0)
                    _inventoriesByBlock.Add(block, trackedInventories);
            }

            for (var i = trackedInventories.Count - 1; i >= 0; i--)
            {
                var inventory = trackedInventories[i];
                if (currentInventories.Contains(inventory))
                    continue;

                trackedInventories.RemoveAt(i);
                UnbindInventory(inventory);
            }

            foreach (var inventory in currentInventories)
            {
                if (trackedInventories.Contains(inventory))
                    continue;

                trackedInventories.Add(inventory);
                BindInventory(inventory);
            }

            if (trackedInventories.Count == 0)
                _inventoriesByBlock.Remove(block);
        }

        void AddFarmPlot(IMyCubeBlock block)
        {
            if (block == null || _farmPlotsByBlock.ContainsKey(block))
                return;

            var functionalBlock = block as IMyFunctionalBlock;
            if (functionalBlock == null)
                return;

            IMyFarmPlotLogic farmLogic = null;
            IMyResourceStorageComponent storage = null;
            foreach (var component in block.Components)
            {
                if (farmLogic == null)
                    farmLogic = component as IMyFarmPlotLogic;
                if (storage == null)
                    storage = component as IMyResourceStorageComponent;
                if (farmLogic != null && storage != null)
                    break;
            }

            if (farmLogic == null || storage == null)
                return;

            var entry = new FarmPlotEntry(functionalBlock, farmLogic, storage);
            _farmPlotsByBlock.Add(block, entry);
            _farmPlots.Add(entry);
        }

        void RemoveFarmPlot(IMyCubeBlock block)
        {
            FarmPlotEntry entry;
            if (block == null || !_farmPlotsByBlock.TryGetValue(block, out entry))
                return;

            _farmPlotsByBlock.Remove(block);
            _farmPlots.Remove(entry);
        }

        void RemoveBlockInventories(IMyCubeBlock block)
        {
            List<MyInventoryBase> inventories;
            if (!_inventoriesByBlock.TryGetValue(block, out inventories))
                return;

            _inventoriesByBlock.Remove(block);
            foreach (var inventory in inventories)
                UnbindInventory(inventory);
        }

        void BindInventory(MyInventoryBase inventory)
        {
            _items.AddInventory(inventory);
            inventory.InventoryContentChanged += OnInventoryContentChanged;
            inventory.ContentsChanged += OnInventoryContentsChanged;
        }

        void UnbindInventory(MyInventoryBase inventory)
        {
            inventory.InventoryContentChanged -= OnInventoryContentChanged;
            inventory.ContentsChanged -= OnInventoryContentsChanged;
            if (_items != null)
                _items.RemoveInventory(inventory);
        }

        void OnInventoryContentChanged(
            MyInventoryBase inventory,
            MyPhysicalInventoryItem item,
            MyFixedPoint amount)
        {
            if (_items == null)
                return;

#if EXPERIMENTAL
            using (RuntimeProfiler.Measure(
                       "event.inventory",
                       "content_changed",
                       null,
                       inventory.Entity?.EntityId ?? TargetGrid))
#endif
            {
                if (item.Content != null)
                    _items.ScheduleRecalculation(inventory, (MyItemType)item.Content);
            }
        }

        void OnInventoryContentsChanged(MyInventoryBase inventory)
        {
            if (_items == null)
                return;

#if EXPERIMENTAL
            using (RuntimeProfiler.Measure(
                       "event.inventory",
                       "contents_changed",
                       null,
                       inventory.Entity?.EntityId ?? TargetGrid))
#endif
            {
                _items.ScheduleInventoryRecalculation(inventory);
            }
        }

        void ScheduleInventoryTopologyRefresh()
        {
            if (_isUnloaded || _inventoryTopologyRefreshScheduled)
                return;

            _inventoryTopologyRefreshScheduled = true;
            LcdModClientComponent.RunNextFrame.Add(RefreshInventoryTopology);
        }

        void RefreshInventoryTopology()
        {
#if EXPERIMENTAL
            using (RuntimeProfiler.Measure("items.topology", "refresh", null, TargetGrid))
#endif
            {
                _inventoryTopologyRefreshScheduled = false;
                if (!IsAlive || (_items == null && !HasInventoryTopologyNeed))
                    return;

                try
                {
                    if (_items != null)
                        RefreshInventoryTopologyCore();
                    RaiseInventoryTopologyChanged();
                }
                catch (Exception e)
                {
                    LogHelper.LogOnce(
                        "GridLogic.RefreshInventoryTopology." + TargetGrid + "." + e.GetType().FullName,
                        "Inventory topology refresh failed and will be retried: " + e);
                    ScheduleInventoryTopologyRefresh();
                }
            }
        }

        void RefreshInventoryTopologyCore()
        {
            var blocks = new List<IMySlimBlock>();
            Grid.GetBlocks(blocks);
            var currentBlocks = new HashSet<IMyCubeBlock>();
            foreach (var block in blocks)
            {
                var fatBlock = block.FatBlock;
                if (fatBlock == null)
                    continue;

                currentBlocks.Add(fatBlock);
                if (_blocks != null && !_blocks.All.Contains(fatBlock))
                    _blocks.Add(fatBlock);
                if (_blocks != null)
                    AddFarmPlot(fatBlock);
                if (_items != null)
                    RefreshBlockInventories(fatBlock);
            }

            if (_items != null)
            {
                var removedInventoryBlocks = _inventoriesByBlock.Keys
                    .Where(block => !currentBlocks.Contains(block))
                    .ToList();
                foreach (var block in removedInventoryBlocks)
                    RemoveBlockInventories(block);
            }

            if (_blocks != null)
            {
                var removedBlocks = _blocks.All
                    .Where(block => !currentBlocks.Contains(block))
                    .ToList();
                foreach (var block in removedBlocks)
                {
                    if (_items != null)
                        RemoveBlockInventories(block);
                    RemoveFarmPlot(block);
                    _blocks.Remove(block);
                }
            }
        }

        void RaiseInventoryTopologyChanged()
        {
            var handlers = InventoryTopologyChanged;
            if (handlers == null)
                return;

            Exception firstError = null;
            var args = new InventoryTopologyChangedArgs();
            foreach (var @delegate in handlers.GetInvocationList())
            {
                var handler = (InventoryTopologyChanged)@delegate;
                try
                {
                    handler(this, args);
                }
                catch (Exception e)
                {
                    if (firstError == null)
                        firstError = e;
                }
            }

            if (firstError != null)
                throw firstError;
        }

    }

    public delegate void GridGroupChanged(object sender, GridGroupChangedArgs args);

    public class GridGroupChangedArgs
    {
    }

    public delegate void GridLinkChanged(object sender, GridLinkChangedArgs args);

    public sealed class GridLinkChangedArgs
    {
        public GridLinkChangedArgs(GridLinkTypeEnum linkType)
        {
            LinkType = linkType;
        }

        public GridLinkTypeEnum LinkType { get; private set; }
    }

    public delegate void TerminalGroupChanged(object sender, TerminalGroupChangedArgs args);

    public sealed class TerminalGroupChangedArgs
    {
        public TerminalGroupChangedArgs(string groupName)
        {
            GroupName = groupName;
        }

        public string GroupName { get; private set; }
    }

    public delegate void InventoryTopologyChanged(object sender, InventoryTopologyChangedArgs args);

    public sealed class InventoryTopologyChangedArgs
    {
    }
}
