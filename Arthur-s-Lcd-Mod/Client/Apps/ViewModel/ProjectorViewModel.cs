using System;
using System.Collections.Generic;
using LcdMod.Client.GridData;
using LcdMod.Client.Helpers;
using LcdMod.Common.Config.Components;
using LcdMod.Common.Helpers;
using LcdMod.Common.Mvvm;
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

        bool _isDisposed;
        bool _showIngots;
        int _projectionVersion;

        public ProjectorViewModel(
            GridLogic gridLogic,
            ItemSelectionConfigComponent selection,
            BlockSelectionConfigComponent blockSelection)
        {
            Items = new ObservableList<ItemEntry>();
            _availableItems = new ItemsAppViewModel(gridLogic, MaterialSelection, blockSelection);
            _availableItems.Items.ItemAdded += OnAvailableItemAdded;
            _availableItems.Items.ItemRemoved += OnAvailableItemRemoved;
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

        public IReadOnlyDictionary<MyItemType, double> ComponentMissing => _componentMissing;

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
        }

        public bool UpdateProjector(IMyCubeGrid rootGrid, long projectorEntityId, bool showIngots)
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

            _showIngots = showIngots;
            ReadProjectorRequirements();
            RebuildAvailableAmounts();
            var itemsChanged = RebuildProjection(showIngots);

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
            _availableItems.Items.ItemAdded -= OnAvailableItemAdded;
            _availableItems.Items.ItemRemoved -= OnAvailableItemRemoved;
            foreach (var item in _availableItems.Items)
                UnbindAvailableItem(item);
            _availableItems.Dispose();
        }

        void OnAvailableItemAdded(IObservableCollection<ItemEntry> sender, ItemEntry item)
        {
            BindAvailableItem(item);
            RebuildAvailableAmounts();
            RebuildProjection(_showIngots);
        }

        void OnAvailableItemRemoved(IObservableCollection<ItemEntry> sender, ItemEntry item)
        {
            UnbindAvailableItem(item);
            RebuildAvailableAmounts();
            RebuildProjection(_showIngots);
        }

        void BindAvailableItem(ItemEntry item)
        {
            if (item != null)
                item.PropertyChanged += OnAvailableItemChanged;
        }

        void UnbindAvailableItem(ItemEntry item)
        {
            if (item != null)
                item.PropertyChanged -= OnAvailableItemChanged;
        }

        void OnAvailableItemChanged(ObservableObject sender, string propertyName)
        {
            if (propertyName != nameof(ItemEntry.Amount))
                return;

            RebuildAvailableAmounts();
            RebuildProjection(_showIngots);
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

        void ReadProjectorRequirements()
        {
            _componentNeeded.Clear();
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
                    if (definition != null)
                        AccumulateComponents(definition, block.Value);
                }

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
                }
            }
            catch (Exception e)
            {
                ErrorHandlerHelper.LogError(e, GetType());
            }
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

        bool RebuildProjection(bool showIngots)
        {
            var previousTotal = TotalMaterials;
            var previousMissing = MissingMaterials;
            var previousComponentMissing = MissingComponents;
            BuildComponentMissing();
            bool changed;
            if (showIngots)
            {
                BuildIngotNeeded();
                changed = ApplyActiveProjection(_ingotNeeded);
            }
            else
            {
                changed = ApplyActiveProjection(_componentNeeded);
            }

            if (changed ||
                previousTotal != TotalMaterials ||
                previousMissing != MissingMaterials ||
                previousComponentMissing != MissingComponents)
                RaiseProjectionChanged();
            return changed;
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
