using System;
using System.Collections.Generic;
using System.Linq;
using LcdMod.Client.GridData;
using LcdMod.Common.Config.Components;
using LcdMod.Common.Helpers;
using LcdMod.Common.Mvvm;
using Sandbox.ModAPI;
using VRage;
using VRage.Game.Entity;
using GridLinkTypeEnum = VRage.Game.ModAPI.GridLinkTypeEnum;
using IMyCubeGrid = VRage.Game.ModAPI.IMyCubeGrid;
using IMyTerminalBlock = Sandbox.ModAPI.IMyTerminalBlock;
using MyItemType = VRage.Game.ModAPI.Ingame.MyItemType;

namespace LcdMod.Client.Apps.ViewModel
{
    /// <summary>
    /// Observable projection of the items tracked by the configured grid or block scope.
    /// </summary>
    public sealed class ItemsAppViewModel : ObservableObject, IItemsAppViewModel
    {
        const int INVENTORY_RECONCILIATION_INTERVAL_FRAMES = 600;

        readonly GridLogic _rootGridLogic;
        readonly List<IMyCubeGrid> _linkedGrids = new List<IMyCubeGrid>();
        readonly HashSet<ObservableDictionary<MyItemType, MyFixedPoint>> _boundSubtypeSources =
            new HashSet<ObservableDictionary<MyItemType, MyFixedPoint>>();
        readonly Dictionary<MyItemType, MyFixedPoint> _aggregateAmounts =
            new Dictionary<MyItemType, MyFixedPoint>();

        readonly HashSet<MyItemType> _selectedSubtypes = new HashSet<MyItemType>();
        readonly TypedItemCollection _scopedItems = new TypedItemCollection();
        readonly HashSet<MyInventoryBase> _boundInventories = new HashSet<MyInventoryBase>();
        readonly List<IMyTerminalBlock> _boundBlocks = new List<IMyTerminalBlock>();
        readonly List<IMyTerminalBlock> _terminalGroupBlocks = new List<IMyTerminalBlock>();
        readonly HashSet<long> _terminalGroupGridIds = new HashSet<long>();
        readonly HashSet<long> _blockScopeGridIds = new HashSet<long>();
        readonly HashSet<GridLogic> _topologySources = new HashSet<GridLogic>();
        readonly HashSet<GridLogic> _boundGridItemLogics = new HashSet<GridLogic>();

        string[] _selectedTypes = Array.Empty<string>();
        string[] _selectionDefinitions = Array.Empty<string>();
        long[] _selectedBlocks = Array.Empty<long>();
        string[] _selectedGroups = Array.Empty<string>();
        int _gridLinkTypeInternal = -1;
        bool _hideEmpty = true;
        bool _hasSelection;
        bool _isDisposed;
        bool _blockSourceRebuildScheduled;
        long _nextInventoryReconciliationFrame;

        public ItemsAppViewModel(
            GridLogic gridLogic,
            ItemSelectionConfigComponent selection,
            BlockSelectionConfigComponent blockSelection)
        {
            Items = new ObservableList<ItemEntry>();
            _rootGridLogic = gridLogic;
            if (MyAPIGateway.Session != null)
            {
                var reconciliationSeed =
                    ((gridLogic != null ? gridLogic.TargetGrid : 0L) ^ GetHashCode()) & long.MaxValue;
                _nextInventoryReconciliationFrame =
                    MyAPIGateway.Session.GameplayFrameCounter +
                    reconciliationSeed % INVENTORY_RECONCILIATION_INTERVAL_FRAMES;
            }
            UpdateSelection(selection, blockSelection, true);
        }

        public ObservableList<ItemEntry> Items { get; private set; }

        public int Count => Items.Count;

        public bool HasItems => Count > 0;

        public void UpdateSelection(
            ItemSelectionConfigComponent selection,
            BlockSelectionConfigComponent blockSelection,
            bool hideEmpty)
        {
            if (_isDisposed)
                return;

            var selectedTypes = Copy(selection?.SelectedCategories);
            var selectedDefinitions = Copy(selection?.SelectedDefinition);
            var selectedBlocks = Copy(blockSelection?.SelectedBlocks);
            var selectedGroups = Copy(blockSelection?.SelectedGroups);
            var gridLinkTypeInternal = blockSelection?.GridLinkTypeInternal ?? -1;
            if (_hasSelection &&
                SequenceEquals(_selectedTypes, selectedTypes) &&
                SequenceEquals(_selectionDefinitions, selectedDefinitions) &&
                SequenceEquals(_selectedBlocks, selectedBlocks) &&
                SequenceEquals(_selectedGroups, selectedGroups) &&
                _gridLinkTypeInternal == gridLinkTypeInternal &&
                _hideEmpty == hideEmpty)
            {
                ReconcileScopedInventoryIfDue();
                return;
            }

            UnbindSources();
            _hasSelection = true;
            _selectedTypes = selectedTypes;
            _selectionDefinitions = selectedDefinitions;
            _selectedBlocks = selectedBlocks;
            _selectedGroups = selectedGroups;
            _gridLinkTypeInternal = gridLinkTypeInternal;
            _hideEmpty = hideEmpty;
            RebuildSelectedSubtypes();
            Items.Clear();

            if (_rootGridLogic == null)
            {
                EnsureSelectedEmptyItems();
                RaiseCollectionSizeChanged();
                return;
            }

            if (HasBlockWhitelist())
                BindBlockSources();
            else
                BindGridSources();

            EnsureSelectedEmptyItems();
            ReconcileScopedInventoryIfDue();
            RaiseCollectionSizeChanged();
        }

        public void Dispose()
        {
            if (_isDisposed)
                return;

            _isDisposed = true;
            UnbindSources();
        }

        void BindGridSources()
        {
            GridLinkTypeEnum linkType;
            if (TryGetLinkType(out linkType))
            {
                _rootGridLogic.WatchLinkType(linkType);
                _rootGridLogic.LinkChanged += OnGridLinkChanged;
                _rootGridLogic.GetLinkedGrids(linkType, _linkedGrids);
                EnsureRootGridFallback(_linkedGrids);
            }
            else if (_rootGridLogic.Grid != null)
            {
                _linkedGrids.Add(_rootGridLogic.Grid);
            }

            var seen = new HashSet<long>();
            foreach (var logic in from grid in 
                         _linkedGrids where grid != null && 
                                            !grid.Closed && 
                                            !grid.MarkedForClose && 
                                            seen.Add(grid.EntityId) select 
                                            LcdModSessionComponent.GetOrCreateGridLogic(grid) into
                                            logic where logic != null select logic)
                BindGridItemSource(logic);
        }

        void BindGridItemSource(GridLogic logic)
        {
            if (logic == null)
                return;

            if (!_boundGridItemLogics.Add(logic))
                return;

            logic.RequestCapability(GridCapability.Items);
            try
            {
                BindSubtypeSource(logic.Items.BySubtype);
            }
            catch
            {
                _boundGridItemLogics.Remove(logic);
                logic.Release(GridCapability.Items);
                throw;
            }
        }

        void BindSubtypeSource(ObservableDictionary<MyItemType, MyFixedPoint> source)
        {
            if (source == null || !_boundSubtypeSources.Add(source))
                return;

            source.ItemAdded += OnSubtypeAdded;
            source.ItemChanged += OnSubtypeChanged;
            source.ItemRemoved += OnSubtypeRemoved;
            foreach (var item in source)
            {
                if (MatchesFilter(item.Key))
                    RecalculateAggregateAmount(item.Key);
            }
        }

        void UnbindGridSources()
        {
            if (_rootGridLogic != null)
                _rootGridLogic.LinkChanged -= OnGridLinkChanged;

            foreach (var source in _boundSubtypeSources)
            {
                source.ItemAdded -= OnSubtypeAdded;
                source.ItemChanged -= OnSubtypeChanged;
                source.ItemRemoved -= OnSubtypeRemoved;
            }

            foreach (var logic in _boundGridItemLogics)
                logic.Release(GridCapability.Items);

            _linkedGrids.Clear();
            _boundSubtypeSources.Clear();
            _boundGridItemLogics.Clear();
            _aggregateAmounts.Clear();
        }

        void RebuildGridSources()
        {
            UnbindGridSources();
            Items.Clear();
            BindGridSources();
            EnsureSelectedEmptyItems();
            RaiseCollectionSizeChanged();
        }

        void OnGridLinkChanged(object sender, GridLinkChangedArgs args)
        {
            GridLinkTypeEnum linkType;
            if (!HasBlockWhitelist() && args != null && TryGetLinkType(out linkType) && args.LinkType == linkType)
                RebuildGridSources();
        }

        void OnSubtypeAdded(
            IObservableCollection<KeyValuePair<MyItemType, MyFixedPoint>> sender,
            KeyValuePair<MyItemType, MyFixedPoint> item)
        {
            RecalculateAggregateAmount(item.Key);
        }

        void OnSubtypeChanged(
            IObservableCollection<KeyValuePair<MyItemType, MyFixedPoint>> sender,
            KeyValuePair<MyItemType, MyFixedPoint> item)
        {
            RecalculateAggregateAmount(item.Key);
        }

        void OnSubtypeRemoved(
            IObservableCollection<KeyValuePair<MyItemType, MyFixedPoint>> sender,
            KeyValuePair<MyItemType, MyFixedPoint> item)
        {
            RecalculateAggregateAmount(item.Key);
        }

        void RecalculateAggregateAmount(MyItemType itemType)
        {
            if (!MatchesFilter(itemType))
                return;

            var amount = MyFixedPoint.Zero;
            foreach (var source in _boundSubtypeSources)
            {
                MyFixedPoint sourceAmount;
                if (source.TryGetValue(itemType, out sourceAmount))
                    amount = MyFixedPoint.AddSafe(amount, sourceAmount);
            }

            if (amount <= MyFixedPoint.Zero)
            {
                _aggregateAmounts.Remove(itemType);
                if (!_hideEmpty && _selectedSubtypes.Contains(itemType))
                    UpsertItem(itemType, MyFixedPoint.Zero);
                else
                    RemoveItem(itemType);
                return;
            }

            _aggregateAmounts[itemType] = amount;
            UpsertItem(itemType, amount);
        }

        bool HasBlockWhitelist()
        {
            return _selectedBlocks.Length > 0 || _selectedGroups.Length > 0;
        }

        void BindBlockSources()
        {
            BindSubtypeSource(_scopedItems.BySubtype);
            BindBlockTopologySources();

            if (_selectedGroups.Length > 0)
            {
                _rootGridLogic.TerminalGroupChanged += OnTerminalGroupChanged;
                BindTerminalGroupLinkScope();
            }

            var seenBlocks = new HashSet<long>();
            foreach (var t in _selectedBlocks)
            {
                var block = MyAPIGateway.Entities.GetEntityById(t) as IMyTerminalBlock;
                if (block == null)
                    continue;
                if (IsUsableInventoryBlock(block) &&
                    IsBlockInConfiguredLinkScope(block) &&
                    seenBlocks.Add(block.EntityId))
                    BindBlock(block);
            }

            if (_selectedGroups.Length > 0)
            {
                _rootGridLogic.GetTerminalGroupBlocks(_selectedGroups, _terminalGroupBlocks);
                foreach (var block in _terminalGroupBlocks.Where(block =>
                             IsUsableInventoryBlock(block) &&
                             IsTerminalGroupBlockInLinkScope(block) &&
                             seenBlocks.Add(block.EntityId)))
                    BindBlock(block);
            }
        }

        void BindBlockTopologySources()
        {
            if (_rootGridLogic == null)
                return;

            BindTopologySource(_rootGridLogic);
            _blockScopeGridIds.Clear();
            _linkedGrids.Clear();
            GridLinkTypeEnum linkType;
            if (TryGetLinkType(out linkType))
            {
                _rootGridLogic.WatchLinkType(linkType);
                _rootGridLogic.GetLinkedGrids(linkType, _linkedGrids);
                EnsureRootGridFallback(_linkedGrids);
            }
            else if (_rootGridLogic.Grid != null)
            {
                _linkedGrids.Add(_rootGridLogic.Grid);
            }

            foreach (var grid in _linkedGrids)
            {
                if (grid == null || grid.Closed || grid.MarkedForClose)
                    continue;

                _blockScopeGridIds.Add(grid.EntityId);
                BindTopologySource(LcdModSessionComponent.GetOrCreateGridLogic(grid));
            }
        }

        void BindTopologySource(GridLogic source)
        {
            if (source == null || !_topologySources.Add(source))
                return;

            source.RequestCapability(GridCapability.InventoryTopology);
            source.InventoryTopologyChanged += OnInventoryTopologyChanged;
        }

        void BindTerminalGroupLinkScope()
        {
            _terminalGroupGridIds.Clear();
            _linkedGrids.Clear();

            GridLinkTypeEnum linkType;
            if (TryGetLinkType(out linkType))
            {
                _rootGridLogic.WatchLinkType(linkType);
                _rootGridLogic.LinkChanged += OnTerminalGroupLinkChanged;
                _rootGridLogic.GetLinkedGrids(linkType, _linkedGrids);
                EnsureRootGridFallback(_linkedGrids);
            }
            else if (_rootGridLogic.Grid != null)
            {
                _linkedGrids.Add(_rootGridLogic.Grid);
            }

            foreach (var grid in _linkedGrids)
            {
                if (grid != null && !grid.Closed && !grid.MarkedForClose)
                    _terminalGroupGridIds.Add(grid.EntityId);
            }
        }

        bool IsTerminalGroupBlockInLinkScope(IMyTerminalBlock block)
        {
            return block != null &&
                   block.CubeGrid != null &&
                   _terminalGroupGridIds.Contains(block.CubeGrid.EntityId);
        }

        void BindBlock(IMyTerminalBlock block)
        {
            _boundBlocks.Add(block);
            block.OnMarkForClose += OnBoundBlockClosing;
            for (var inventoryIndex = 0; inventoryIndex < block.InventoryCount; inventoryIndex++)
            {
                var inventory = block.GetInventory(inventoryIndex) as MyInventoryBase;
                if (inventory == null || !_boundInventories.Add(inventory))
                    continue;

                _scopedItems.AddInventory(inventory);
                inventory.InventoryContentChanged += OnScopedInventoryContentChanged;
                inventory.ContentsChanged += OnScopedInventoryContentsChanged;
            }
        }

        void UnbindBlockSources()
        {
            if (_rootGridLogic != null)
            {
                _rootGridLogic.TerminalGroupChanged -= OnTerminalGroupChanged;
                _rootGridLogic.LinkChanged -= OnTerminalGroupLinkChanged;
            }

            foreach (var block in _boundBlocks.Where(block => block != null))
                block.OnMarkForClose -= OnBoundBlockClosing;

            foreach (var inventory in _boundInventories)
            {
                inventory.InventoryContentChanged -= OnScopedInventoryContentChanged;
                inventory.ContentsChanged -= OnScopedInventoryContentsChanged;
            }

            foreach (var source in _topologySources)
            {
                source.InventoryTopologyChanged -= OnInventoryTopologyChanged;
                source.Release(GridCapability.InventoryTopology);
            }

            _scopedItems.Clear();
            _boundBlocks.Clear();
            _boundInventories.Clear();
            _terminalGroupBlocks.Clear();
            _terminalGroupGridIds.Clear();
            _blockScopeGridIds.Clear();
            _linkedGrids.Clear();
            _topologySources.Clear();
        }

        void RebuildBlockSources()
        {
            UnbindBlockSources();
            Items.Clear();
            BindBlockSources();
            EnsureSelectedEmptyItems();
            RaiseCollectionSizeChanged();
        }

        void OnTerminalGroupChanged(object sender, TerminalGroupChangedArgs args)
        {
            if (args == null || string.IsNullOrEmpty(args.GroupName) || Contains(_selectedGroups, args.GroupName))
                ScheduleBlockSourceRebuild();
        }

        void OnTerminalGroupLinkChanged(object sender, GridLinkChangedArgs args)
        {
            GridLinkTypeEnum linkType;
            if (_selectedGroups.Length > 0 &&
                args != null &&
                TryGetLinkType(out linkType) &&
                args.LinkType == linkType)
                ScheduleBlockSourceRebuild();
        }

        void OnBoundBlockClosing(VRage.ModAPI.IMyEntity entity)
        {
            ScheduleBlockSourceRebuild();
        }

        void OnScopedInventoryContentChanged(
            MyInventoryBase inventory,
            MyPhysicalInventoryItem item,
            MyFixedPoint amount)
        {
            {
                if (item.Content != null)
                    _scopedItems.ScheduleRecalculation(inventory, (MyItemType)item.Content);
            }
        }

        void OnScopedInventoryContentsChanged(MyInventoryBase inventory)
        {
            {
                _scopedItems.ScheduleInventoryRecalculation(inventory);
            }
        }

        void OnInventoryTopologyChanged(object sender, InventoryTopologyChangedArgs args)
        {
            ScheduleBlockSourceRebuild();
        }

        void ScheduleBlockSourceRebuild()
        {
            if (_isDisposed || !HasBlockWhitelist() || _blockSourceRebuildScheduled)
                return;

            _blockSourceRebuildScheduled = true;
            LcdModClientComponent.RunNextFrame.Add(RunScheduledBlockSourceRebuild);
        }

        void RunScheduledBlockSourceRebuild()
        {
            {
                _blockSourceRebuildScheduled = false;
                if (_isDisposed || !HasBlockWhitelist())
                    return;

                try
                {
                    RebuildBlockSources();
                }
                catch (Exception e)
                {
                    LogHelper.LogOnce(
                        "ItemsAppViewModel.RebuildBlockSources." + e.GetType().FullName,
                        "Items block source rebuild failed and will be retried: " + e);
                    ScheduleBlockSourceRebuild();
                }
            }
        }

        void ReconcileScopedInventoryIfDue()
        {
            if (!HasBlockWhitelist() || MyAPIGateway.Session == null)
                return;

            var currentFrame = MyAPIGateway.Session.GameplayFrameCounter;
            if (currentFrame < _nextInventoryReconciliationFrame)
                return;

            _nextInventoryReconciliationFrame = currentFrame + INVENTORY_RECONCILIATION_INTERVAL_FRAMES;
            _scopedItems.ReconcileNextInventory();
        }

        void UnbindSources()
        {
            UnbindGridSources();
            UnbindBlockSources();
        }

        static bool IsUsableInventoryBlock(IMyTerminalBlock block)
        {
            return block != null && !block.Closed && !block.MarkedForClose && block.HasInventory;
        }

        bool IsBlockInConfiguredLinkScope(IMyTerminalBlock block)
        {
            return block != null &&
                   block.CubeGrid != null &&
                   _blockScopeGridIds.Contains(block.CubeGrid.EntityId);
        }

        void EnsureRootGridFallback(List<IMyCubeGrid> grids)
        {
            if (grids == null || grids.Count > 0 || _rootGridLogic?.Grid == null)
                return;

            grids.Add(_rootGridLogic.Grid);
        }

        bool TryGetLinkType(out GridLinkTypeEnum linkType)
        {
            switch (_gridLinkTypeInternal)
            {
                case (int)GridLinkTypeEnum.Mechanical:
                    linkType = GridLinkTypeEnum.Mechanical;
                    return true;
                case (int)GridLinkTypeEnum.Physical:
                    linkType = GridLinkTypeEnum.Physical;
                    return true;
                default:
                    linkType = default(GridLinkTypeEnum);
                    return false;
            }
        }

        void UpsertItem(MyItemType itemType, MyFixedPoint amount)
        {
            var index = IndexOf(itemType);
            if (index >= 0)
            {
                Items[index].Amount = amount;
                return;
            }

            Items.Add(new ItemEntry(itemType, amount));
            RaiseCollectionSizeChanged();
        }

        void RemoveItem(MyItemType itemType)
        {
            var index = IndexOf(itemType);
            if (index < 0)
                return;

            Items.RemoveAt(index);
            RaiseCollectionSizeChanged();
        }

        bool MatchesFilter(MyItemType itemType)
        {
            if (_selectedTypes.Length == 0 && _selectedSubtypes.Count == 0)
                return true;

            return MatchesSelectedType(itemType.TypeId) || _selectedSubtypes.Contains(itemType);
        }

        bool MatchesSelectedType(string typeId) => _selectedTypes.Any(t => typeId.EndsWith(t, StringComparison.OrdinalIgnoreCase));

        void RebuildSelectedSubtypes()
        {
            _selectedSubtypes.Clear();
            foreach (var definition in _selectionDefinitions)
            {
                try
                {
                    _selectedSubtypes.Add(MyItemType.Parse(definition));
                }
                catch
                {
                    LogHelper.LogInfo($"Failed to parse item type definition '{definition}' for selection.");
                }
            }
        }

        void EnsureSelectedEmptyItems()
        {
            if (_hideEmpty)
                return;

            foreach (var itemType in _selectedSubtypes)
            {
                MyFixedPoint amount;
                _aggregateAmounts.TryGetValue(itemType, out amount);
                UpsertItem(itemType, amount);
            }
        }

        int IndexOf(MyItemType itemType)
        {
            for (var i = 0; i < Items.Count; i++)
            {
                if (Items[i].ItemType.Equals(itemType))
                    return i;
            }

            return -1;
        }

        void RaiseCollectionSizeChanged()
        {
            RaisePropertyChanged<int>(nameof(Count));
            RaisePropertyChanged<bool>(nameof(HasItems));
        }

        static string[] Copy(string[] source)
        {
            return source == null || source.Length == 0 ? Array.Empty<string>() : (string[])source.Clone();
        }

        static long[] Copy(long[] source)
        {
            return source == null || source.Length == 0 ? Array.Empty<long>() : (long[])source.Clone();
        }

        static bool Contains(string[] values, string value)
        {
            for (var i = 0; values != null && i < values.Length; i++)
            {
                if (string.Equals(values[i], value, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        static bool SequenceEquals(string[] left, string[] right)
        {
            if (ReferenceEquals(left, right))
                return true;
            if (left == null || right == null || left.Length != right.Length)
                return false;

            for (var i = 0; i < left.Length; i++)
            {
                if (!string.Equals(left[i], right[i], StringComparison.Ordinal))
                    return false;
            }

            return true;
        }

        static bool SequenceEquals(long[] left, long[] right)
        {
            if (ReferenceEquals(left, right))
                return true;
            if (left == null || right == null || left.Length != right.Length)
                return false;

            for (var i = 0; i < left.Length; i++)
            {
                if (left[i] != right[i])
                    return false;
            }

            return true;
        }
    }
}
