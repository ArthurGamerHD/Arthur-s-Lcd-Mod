using ListMode = LcdMod.Client.Apps.Projector.ProjectorListMode;
using System;
using Adk.Mvvm;
using System.Collections.Generic;
using LcdMod.Client.GridData;
using LcdMod.Client.Helpers;
using LcdMod.Common.Config.Components;
using LcdMod.Common.Helpers;
using Sandbox.Definitions;
using Sandbox.ModAPI;
using VRage;
using VRage.Game;
using VRage.Game.ModAPI;
using IMyCubeGrid = VRage.Game.ModAPI.IMyCubeGrid;
using MyItemType = VRage.Game.ModAPI.Ingame.MyItemType;

namespace LcdMod.Client.Apps.ViewModel
{
    /// <summary>
    /// Projects the inventories selected by the items framework into the materials
    /// still missing from a projector blueprint.
    /// </summary>
    public sealed class ProjectorViewModel : ObservableObject, IItemsAppViewModel
    {
        const string INGOT_TYPE_ID = "MyObjectBuilder_Ingot";

        static readonly ItemSelectionConfigComponent MaterialSelection = new ItemSelectionConfigComponent
        {
            SelectedCategories = new[] { "Component", "Ingot" }
        };
        static readonly Dictionary<MyDefinitionId, MyBlueprintDefinitionBase> ComponentBlueprints =
            new Dictionary<MyDefinitionId, MyBlueprintDefinitionBase>();
        static bool _componentBlueprintsInitialized;

        readonly ItemsAppViewModel _availableItems;
        readonly HashSet<ItemEntry> _boundAvailableItems = new HashSet<ItemEntry>();
        readonly List<IMyCubeGrid> _projectorGrids = new List<IMyCubeGrid>();
        readonly List<IMySlimBlock> _projectorBlocks = new List<IMySlimBlock>();
        readonly Dictionary<MyItemType, MyFixedPoint> _available =
            new Dictionary<MyItemType, MyFixedPoint>();
        readonly Dictionary<MyItemType, double> _componentNeeded =
            new Dictionary<MyItemType, double>();
        readonly Dictionary<MyItemType, double> _componentMissing =
            new Dictionary<MyItemType, double>();
        readonly Dictionary<MyItemType, double> _ingotNeeded =
            new Dictionary<MyItemType, double>();
        readonly HashSet<MyItemType> _activeTypes = new HashSet<MyItemType>();
        readonly Dictionary<MyItemType, double> _blockRemaining = new Dictionary<MyItemType, double>();
        readonly Dictionary<MyItemType, double> _blockTotal = new Dictionary<MyItemType, double>();
        readonly Dictionary<MyItemType, MyCubeBlockDefinition> _blockDefinitions =
            new Dictionary<MyItemType, MyCubeBlockDefinition>();

        bool _isDisposed;
        ListMode _mode;
        int _projectionVersion;

        public ProjectorViewModel(
            GridLogic gridLogic,
            ItemSelectionConfigComponent selection,
            BlockSelectionConfigComponent blockSelection)
        {
            Items = new ObservableList<ItemEntry>();
            _availableItems = new ItemsAppViewModel(gridLogic, MaterialSelection, blockSelection);
            _availableItems.Items.CollectionChanged += OnAvailableItemsChanged;
            foreach (var item in _availableItems.Items)
                BindAvailableItem(item);

            RebuildAvailableAmounts();
        }

        public ObservableList<ItemEntry> Items { get; private set; }

        public bool HasItems => Items.Count > 0;

        public IMyProjector Projector { get; private set; }

        public int TotalBlocks { get; private set; } = 1;

        public int RemainingBlocks { get; private set; }

        public int TotalMaterials { get; private set; }

        public int MissingMaterials { get; private set; }

        public int MissingComponents { get; private set; }

        public double TotalComponents { get; private set; }
        public double AvailableComponents { get; private set; }
        public double TotalIngots { get; private set; }
        public double AvailableIngots { get; private set; }

        public IReadOnlyDictionary<MyItemType, double> ComponentMissing => _componentMissing;

        /// <summary>How many blocks of this type the projection still needs.</summary>
        public int GetRemainingBlocks(MyItemType type)
        {
            double remaining;
            return _blockRemaining.TryGetValue(type, out remaining) ? (int)Math.Round(remaining) : 0;
        }

        /// <summary>The block definition behind a Blocks-mode entry.</summary>
        public bool TryGetBlockDefinition(MyItemType type, out MyCubeBlockDefinition definition)
        {
            return _blockDefinitions.TryGetValue(type, out definition);
        }

        public int ProjectionVersion => _projectionVersion;

        public void UpdateSelection(
            ItemSelectionConfigComponent selection,
            BlockSelectionConfigComponent blockSelection,
            bool hideEmpty)
        {
            if (_isDisposed)
                return;

            // Availability changes are projected by the observable item callbacks below.
            // Projector requirements themselves are refreshed once by UpdateProjector.
            _availableItems.UpdateSelection(MaterialSelection, blockSelection, true);
            if (_hideEmpty == hideEmpty)
                return;
            _hideEmpty = hideEmpty;
            RebuildProjection();
        }

        // "Hide empty" hides requirements with nothing missing (all of them once the projection is complete).
        bool _hideEmpty;

        public bool UpdateProjector(IMyCubeGrid rootGrid, long projectorEntityId, ListMode mode)
        {
            if (_isDisposed)
                return false;

            var previousProjectionVersion = _projectionVersion;
            var previousProjector = Projector;
            Projector = ResolveProjector(rootGrid, projectorEntityId);

            var previousTotalBlocks = TotalBlocks;
            var previousRemainingBlocks = RemainingBlocks;
            var previousTotalMaterials = TotalMaterials;
            var previousMissingMaterials = MissingMaterials;
            var previousMissingComponents = MissingComponents;

            if (_mode != mode)
                Items.Clear();
            _mode = mode;
            ReadProjectorRequirements();
            RebuildAvailableAmounts();
            var itemsChanged = RebuildProjection();

            var changed = itemsChanged ||
                          !ReferenceEquals(previousProjector, Projector) ||
                          previousTotalBlocks != TotalBlocks ||
                          previousRemainingBlocks != RemainingBlocks ||
                          previousTotalMaterials != TotalMaterials ||
                          previousMissingMaterials != MissingMaterials ||
                          previousMissingComponents != MissingComponents;
            if (changed && _projectionVersion == previousProjectionVersion)
                RaiseProjectionChanged();
            return changed;
        }

        public void Dispose()
        {
            if (_isDisposed)
                return;

            _isDisposed = true;
            _availableItems.Items.CollectionChanged -= OnAvailableItemsChanged;
            ClearAvailableItemSubscriptions();
            _availableItems.Dispose();
        }

        void OnAvailableItemsChanged(object sender, NotifyCollectionChangedEventArgs args)
        {
            if (args.Action == NotifyCollectionChangedAction.Move)
                return;
            if (args.Action == NotifyCollectionChangedAction.Reset)
            {
                ClearAvailableItemSubscriptions();
                foreach (var item in _availableItems.Items)
                    BindAvailableItem(item);
            }
            if (args.OldItems != null)
                foreach (ItemEntry item in args.OldItems)
                    UnbindAvailableItem(item);
            if (args.NewItems != null)
                foreach (ItemEntry item in args.NewItems)
                    BindAvailableItem(item);
            RebuildAvailableAmounts();
            RebuildProjection();
        }

        void ClearAvailableItemSubscriptions()
        {
            foreach (var item in _boundAvailableItems)
                item.PropertyChanged -= OnAvailableItemChanged;
            _boundAvailableItems.Clear();
        }

        void BindAvailableItem(ItemEntry item)
        {
            if (item != null && _boundAvailableItems.Add(item))
                item.PropertyChanged += OnAvailableItemChanged;
        }

        void UnbindAvailableItem(ItemEntry item)
        {
            if (item != null && _boundAvailableItems.Remove(item))
                item.PropertyChanged -= OnAvailableItemChanged;
        }

        void OnAvailableItemChanged(ObservableObject sender, string propertyName)
        {
            if (propertyName != nameof(ItemEntry.Amount))
                return;

            RebuildAvailableAmounts();
            RebuildProjection();
        }

        void RebuildAvailableAmounts()
        {
            _available.Clear();
            foreach (var item in _availableItems.Items)
            {
                if (item != null)
                    _available[item.ItemType] = item.Amount;
            }
        }

        // A finished projection lists the blueprint's components as fully satisfied (the inventory's "Hide empty"
        // filter decides whether they show), instead of an empty list.
        bool _projectionComplete;

        void ReadProjectorRequirements()
        {
            _componentNeeded.Clear();
            _blockRemaining.Clear();
            _blockTotal.Clear();
            _projectionComplete = false;
            TotalBlocks = 1;
            RemainingBlocks = 0;

            if (Projector == null)
                return;

            try
            {
                TotalBlocks = Math.Max(Projector.TotalBlocks, 1);
                RemainingBlocks = Math.Max(Projector.RemainingBlocks, 0);
            }
            catch
            {
                TotalBlocks = 1;
                RemainingBlocks = 0;
            }

            try
            {
                foreach (var block in Projector.RemainingBlocksPerType)
                {
                    var definition = block.Key as MyCubeBlockDefinition;
                    if (definition == null)
                        continue;
                    AccumulateComponents(definition, block.Value);
                    AccumulateBlock(_blockRemaining, definition, block.Value);
                }

                if (_mode == ListMode.Blocks && Projector.ProjectedGrid != null)
                    ReadBlockTotals();

                if (_componentNeeded.Count == 0 && Projector.ProjectedGrid != null)
                {
                    _projectorBlocks.Clear();
                    Projector.ProjectedGrid.GetBlocks(_projectorBlocks);
                    foreach (var block in _projectorBlocks)
                    {
                        var definition = block.BlockDefinition as MyCubeBlockDefinition;
                        if (definition != null)
                            AccumulateComponents(definition, 1);
                    }
                    _projectionComplete = RemainingBlocks == 0;
                }
            }
            catch (Exception e)
            {
                ErrorHandlerHelper.LogError(e, GetType());
            }
        }

        // The projected grid holds the whole blueprint, so it gives each block type's total.
        void ReadBlockTotals()
        {
            _projectorBlocks.Clear();
            Projector.ProjectedGrid.GetBlocks(_projectorBlocks);
            foreach (var block in _projectorBlocks)
            {
                var definition = block.BlockDefinition as MyCubeBlockDefinition;
                if (definition != null)
                    AccumulateBlock(_blockTotal, definition, 1);
            }
            foreach (var remaining in _blockRemaining)
            {
                double total;
                _blockTotal.TryGetValue(remaining.Key, out total);
                if (total < remaining.Value)
                    _blockTotal[remaining.Key] = remaining.Value;
            }
        }

        void AccumulateBlock(Dictionary<MyItemType, double> counts, MyCubeBlockDefinition definition, int count)
        {
            MyItemType type = definition.Id;
            _blockDefinitions[type] = definition;
            double current;
            counts.TryGetValue(type, out current);
            counts[type] = current + count;
        }

        void AccumulateComponents(MyCubeBlockDefinition definition, int blockCount)
        {
            if (definition.Components == null)
                return;

            foreach (var component in definition.Components)
            {
                double current;
                _componentNeeded.TryGetValue(component.Definition.Id, out current);
                _componentNeeded[component.Definition.Id] = current + component.Count * blockCount;
            }
        }

        bool RebuildProjection()
        {
            var previousTotal = TotalMaterials;
            var previousMissing = MissingMaterials;
            var previousComponentMissing = MissingComponents;
            var previousTotalIngots = TotalIngots;
            var previousAvailableIngots = AvailableIngots;
            var previousTotalComponents = TotalComponents;
            var previousAvailableComponents = AvailableComponents;
            BuildComponentMissing();
            // Footer completion stages are independent of the selected item view.
            BuildIngotNeeded();
            TotalComponents = SumRequirements(_componentNeeded, false);
            AvailableComponents = SumRequirements(_componentNeeded, true);
            TotalIngots = SumRequirements(_ingotNeeded, false);
            AvailableIngots = SumRequirements(_ingotNeeded, true);
            bool changed;
            if (_mode == ListMode.Blocks)
                changed = ApplyBlockProjection();
            else if (_mode == ListMode.Ingots)
                changed = ApplyActiveProjection(_ingotNeeded);
            else
                changed = ApplyActiveProjection(_componentNeeded);

            if (changed ||
                previousTotal != TotalMaterials ||
                previousMissing != MissingMaterials ||
                previousComponentMissing != MissingComponents ||
                previousTotalIngots != TotalIngots || previousAvailableIngots != AvailableIngots ||
                previousTotalComponents != TotalComponents || previousAvailableComponents != AvailableComponents)
                RaiseProjectionChanged();
            return changed;
        }

        double SumRequirements(Dictionary<MyItemType, double> requirements, bool available)
        {
            double total = 0d;
            foreach (var requirement in requirements)
                total += available ? Math.Min(requirement.Value, Math.Max(0d, GetAvailable(requirement.Key))) : requirement.Value;
            return total;
        }

        void RaiseProjectionChanged()
        {
            _projectionVersion++;
            RaisePropertyChanged<int>(nameof(ProjectionVersion));
        }

        void BuildComponentMissing()
        {
            _componentMissing.Clear();
            long totalMissing = 0;
            foreach (var needed in _componentNeeded)
            {
                var missing = Math.Max(0d, needed.Value - GetAvailable(needed.Key));
                _componentMissing[needed.Key] = missing;
                totalMissing += (long)Math.Round(missing);
            }

            MissingComponents = (int)Math.Max(0, totalMissing);
        }

        bool ApplyActiveProjection(Dictionary<MyItemType, double> neededByType)
        {
            _activeTypes.Clear();
            long total = 0;
            long missingTotal = 0;
            bool changed = false;

            foreach (var needed in neededByType)
            {
                var available = GetAvailable(needed.Key);
                var missing = Math.Max(0d, needed.Value - available);
                total += (long)Math.Round(needed.Value);
                missingTotal += (long)Math.Round(missing);
                if (_hideEmpty && missing <= 0d)
                    continue;

                _activeTypes.Add(needed.Key);
                changed |= UpsertItem(needed.Key, available, needed.Value, missing);
            }

            for (var i = Items.Count - 1; i >= 0; i--)
            {
                if (_activeTypes.Contains(Items[i].ItemType))
                    continue;

                Items.RemoveAt(i);
                changed = true;
            }

            TotalMaterials = (int)Math.Max(0, total);
            MissingMaterials = (int)Math.Max(0, missingTotal);
            return changed;
        }

        bool ApplyBlockProjection()
        {
            _activeTypes.Clear();
            long total = 0;
            long missingTotal = 0;
            bool changed = false;
            foreach (var block in _blockTotal)
            {
                double remaining;
                _blockRemaining.TryGetValue(block.Key, out remaining);
                total += (long)Math.Round(block.Value);
                missingTotal += (long)Math.Round(remaining);
                if (_hideEmpty && remaining <= 0d)
                    continue;
                _activeTypes.Add(block.Key);
                changed |= UpsertItem(block.Key, block.Value - remaining, block.Value, remaining);
            }

            for (var i = Items.Count - 1; i >= 0; i--)
            {
                if (_activeTypes.Contains(Items[i].ItemType))
                    continue;
                Items.RemoveAt(i);
                changed = true;
            }

            TotalMaterials = (int)Math.Max(0, total);
            MissingMaterials = (int)Math.Max(0, missingTotal);
            return changed;
        }

        bool UpsertItem(MyItemType itemType, double available, double needed, double missing)
        {
            var item = FindItem(itemType);
            bool changed = false;
            if (item == null)
            {
                item = new ItemEntry(itemType, (MyFixedPoint)missing);
                Items.Add(item);
                changed = true;
            }

            var amount = (MyFixedPoint)missing;
            if (item.Amount != amount)
            {
                item.Amount = amount;
                changed = true;
            }

            item.CraftAmount = Math.Max(1d, Math.Ceiling(missing));
            item.AvailabilityStatus = available <= 0d
                ? ItemAvailabilityStatus.Error
                : available < needed
                    ? ItemAvailabilityStatus.Warning
                    : ItemAvailabilityStatus.Normal;
            var cappedAvailable = Math.Min(available, needed);
            item.SetQuotaAmount(
                FormatingHelper.FormatItemQty(cappedAvailable),
                FormatingHelper.FormatItemQty(needed));
            return changed;
        }

        ItemEntry FindItem(MyItemType itemType)
        {
            foreach (var item in Items)
            {
                if (item.ItemType.Equals(itemType))
                    return item;
            }

            return null;
        }

        double GetAvailable(MyItemType itemType)
        {
            if (_projectionComplete)
                return double.MaxValue;
            MyFixedPoint amount;
            return _available.TryGetValue(itemType, out amount) ? (double)amount : 0d;
        }

        void BuildIngotNeeded()
        {
            _ingotNeeded.Clear();
            if (_componentNeeded.Count == 0)
                return;

            try
            {
                EnsureComponentBlueprints();
                foreach (var component in _componentNeeded)
                {
                    if (component.Value <= 0d)
                        continue;

                    MyBlueprintDefinitionBase blueprint;
                    if (!ComponentBlueprints.TryGetValue(component.Key, out blueprint) ||
                        blueprint == null)
                        continue;

                    var resultAmount = GetBlueprintResultAmount(blueprint, component.Key);
                    if (resultAmount <= 0d)
                        resultAmount = 1d;
                    var cycles = component.Value / resultAmount;
                    var prerequisites = blueprint.Prerequisites;
                    if (prerequisites == null)
                        continue;

                    foreach (var prerequisite in prerequisites)
                    {
                        MyItemType ingotType = prerequisite.Id;
                        if (ingotType.TypeId != INGOT_TYPE_ID)
                            continue;

                        double current;
                        _ingotNeeded.TryGetValue(ingotType, out current);
                        _ingotNeeded[ingotType] = current + (double)prerequisite.Amount * cycles;
                    }
                }
            }
            catch (Exception e)
            {
                ErrorHandlerHelper.LogError(e, GetType());
            }
        }

        static double GetBlueprintResultAmount(MyBlueprintDefinitionBase blueprint, MyDefinitionId itemId)
        {
            var results = blueprint.Results;
            if (results == null)
                return 1d;

            foreach (var result in results)
            {
                if (result.Id.Equals(itemId))
                    return (double)result.Amount;
            }

            return 1d;
        }

        static void EnsureComponentBlueprints()
        {
            if (_componentBlueprintsInitialized || MyDefinitionManager.Static == null)
                return;

            foreach (var blueprint in MyDefinitionManager.Static.GetBlueprintDefinitions())
            {
                var results = blueprint?.Results;
                if (results == null || results.Length == 0)
                    continue;

                var primaryResult = results[0].Id;
                if (blueprint.IsPrimary || !ComponentBlueprints.ContainsKey(primaryResult))
                    ComponentBlueprints[primaryResult] = blueprint;
            }

            _componentBlueprintsInitialized = true;
        }

        IMyProjector ResolveProjector(IMyCubeGrid rootGrid, long projectorEntityId)
        {
            if (rootGrid == null)
                return null;

            if (projectorEntityId != 0)
            {
                var projector = MyAPIGateway.Entities.GetEntityById(projectorEntityId) as IMyProjector;
                return projector != null && projector.CubeGrid.IsInSameLogicalGroupAs(rootGrid)
                    ? projector
                    : null;
            }

            IMyProjector found = null;
            _projectorGrids.Clear();
            MyAPIGateway.GridGroups.GetGroup(rootGrid, GridLinkTypeEnum.Logical, _projectorGrids);
            if (_projectorGrids.Count == 0 || !_projectorGrids.Contains(rootGrid))
                _projectorGrids.Add(rootGrid);

            foreach (var grid in _projectorGrids)
            {
                if (grid == null)
                    continue;

                _projectorBlocks.Clear();
                grid.GetBlocks(_projectorBlocks);
                foreach (var block in _projectorBlocks)
                {
                    var candidate = block.FatBlock as IMyProjector;
                    if (candidate == null || candidate.Closed || candidate.ProjectedGrid == null)
                        continue;

                    if (found != null)
                        return null;

                    found = candidate;
                }
            }

            return found;
        }
    }
}
