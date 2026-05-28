using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using LcdMod.Client.Helpers;
using LcdMod.Common.Helpers;

using Sandbox.Definitions;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.Game.ModAPI.Ingame;
using VRageMath;
using IMyCubeGrid = VRage.Game.ModAPI.IMyCubeGrid;
using IMyFarmPlotLogic = Sandbox.ModAPI.IMyFarmPlotLogic;
using IMyFunctionalBlock = Sandbox.ModAPI.IMyFunctionalBlock;
using IMySlimBlock = VRage.Game.ModAPI.IMySlimBlock;
using IngameItem = VRage.Game.ModAPI.Ingame.MyInventoryItem;
using NotImplementedException = LcdMod.Common.NotImplementedException;
using ScreenConfigWithBlocks = LcdMod.Common.Config.Models.Apps.ScreenConfigWithBlocks;
using ScreenConfigWithItems = LcdMod.Common.Config.Models.Apps.ScreenConfigWithItems;

namespace LcdMod.Client.Grid
{
    /// <summary>
    /// Logic attached to <see cref="Grid"/>
    /// </summary>
    public class GridLogic
    {
        const int DELAY = 120;
        const int REQUEST_TTL_TICKS = 120;
        const int TARGET_REFRESH_TICKS = 119;
        const int REFRESH_BATCH_SIZE = 128;
        static readonly object AssemblerBlueprintDatabaseLock = new object();
        static bool _blueprintResultDatabaseInitialized;
        public static readonly Dictionary<string, HashSet<MyDefinitionId>> CraftableBlueprintsByAssemblerSubtype =
            new Dictionary<string, HashSet<MyDefinitionId>>(StringComparer.Ordinal);
        public static readonly Dictionary<MyDefinitionId, HashSet<string>> AssemblerSubtypesByCraftableBlueprint =
            new Dictionary<MyDefinitionId, HashSet<string>>();
        public static readonly Dictionary<MyDefinitionId, HashSet<MyDefinitionId>> CreatedItemsByBlueprint =
            new Dictionary<MyDefinitionId, HashSet<MyDefinitionId>>();
        public static readonly Dictionary<MyDefinitionId, HashSet<MyDefinitionId>> BlueprintsByCreatedItem =
            new Dictionary<MyDefinitionId, HashSet<MyDefinitionId>>();
        public static readonly Dictionary<MyDefinitionId, MyBlueprintDefinitionBase> PrimaryBlueprintByCreatedItem =
            new Dictionary<MyDefinitionId, MyBlueprintDefinitionBase>();
        long _clock;
        int _ticksSinceRequested = int.MaxValue;

        static readonly HashSet<string> KnowSubtypes = new HashSet<string>();
        static readonly HashSet<string> KnowFarmSubtypes = new HashSet<string>();
        
        public readonly IMyCubeGrid Grid;
        GridGroupLogic _gridGroupResolver;
        List<IMySlimBlock> _blocks = new List<IMySlimBlock>();
        List<IMyTerminalBlock> _invBlocks = new List<IMyTerminalBlock>();
        List<IMySlimBlock> _nextBlocks = new List<IMySlimBlock>();
        List<IMyTerminalBlock> _nextInvBlocks = new List<IMyTerminalBlock>();
        
        List<IMyLaserAntenna> _lasers = new List<IMyLaserAntenna>();
        List<IMyRadioAntenna> _radio = new List<IMyRadioAntenna>();
        List<IMyBeacon> _beacons = new List<IMyBeacon>();
        List<IMyBatteryBlock> _batteries = new List<IMyBatteryBlock>();
        List<IMyJumpDrive> _jumpDrives = new List<IMyJumpDrive>();
        List<IMyAssembler> _assemblers = new List<IMyAssembler>();
        List<IMyTerminalBlock> _terminalBlocks = new List<IMyTerminalBlock>();
        List<IMyCargoContainer> _cargoContainers = new List<IMyCargoContainer>();
        List<IMyGasTank> _gasTanks = new List<IMyGasTank>();
        List<IMyPowerProducer> _powerProducers = new List<IMyPowerProducer>();
        List<FarmPlotEntry> _farmPlots = new List<FarmPlotEntry>();
        List<IMyLaserAntenna> _nextLasers = new List<IMyLaserAntenna>();
        List<IMyRadioAntenna> _nextRadio = new List<IMyRadioAntenna>();
        List<IMyBeacon> _nextBeacons = new List<IMyBeacon>();
        List<IMyBatteryBlock> _nextBatteries = new List<IMyBatteryBlock>();
        List<IMyJumpDrive> _nextJumpDrives = new List<IMyJumpDrive>();
        List<IMyAssembler> _nextAssemblers = new List<IMyAssembler>();
        List<IMyTerminalBlock> _nextTerminalBlocks = new List<IMyTerminalBlock>();
        List<IMyCargoContainer> _nextCargoContainers = new List<IMyCargoContainer>();
        List<IMyGasTank> _nextGasTanks = new List<IMyGasTank>();
        List<IMyPowerProducer> _nextPowerProducers = new List<IMyPowerProducer>();
        List<FarmPlotEntry> _nextFarmPlots = new List<FarmPlotEntry>();
        IEnumerator<bool> _refreshUpdater;
        bool _refreshQueued;
        int _currentRefreshBatchSize = REFRESH_BATCH_SIZE;
        int _nextRefreshBatchSize = REFRESH_BATCH_SIZE;
        int _currentRefreshIterations;
        int _currentRefreshProcessed;
        int _lastRefreshIterations;
        int _lastRefreshProcessed;

        public int LastRefreshIterations => _lastRefreshIterations;
        public int LastRefreshProcessed => _lastRefreshProcessed;
        public int EstimatedNextRefreshBatchSize => _nextRefreshBatchSize;
        public int CurrentRefreshBatchSize => _currentRefreshBatchSize;
        public bool IsRefreshRunning => _refreshUpdater != null;
        public bool IsSleeping => _ticksSinceRequested > REQUEST_TTL_TICKS;

        IMyGridTerminalSystem GridTerminalSystem => MyAPIGateway.TerminalActionsHelper.GetTerminalSystemForGrid(Grid);

        public Dictionary<MyItemType, double> Components
        {
            get
            {
                if (!_compCache.Any())
                    AggregateItems(GetInventories(), _compCache, new[] { "Component" },
                        Array.Empty<MyDefinitionId>());

                return _compCache;
            }
        }

        readonly Dictionary<SearchQueryToken, Dictionary<MyItemType, double>> _queryCache =
            new Dictionary<SearchQueryToken, Dictionary<MyItemType, double>>();

        readonly Dictionary<MyItemType, double> _compCache = new Dictionary<MyItemType, double>();

        readonly Dictionary<SearchQueryToken, Dictionary<MyItemType, double>> _refineryInputQueryCache  = new Dictionary<SearchQueryToken, Dictionary<MyItemType, double>>();
        readonly Dictionary<SearchQueryToken, Dictionary<MyItemType, double>> _refineryOutputQueryCache = new Dictionary<SearchQueryToken, Dictionary<MyItemType, double>>();
        readonly Dictionary<long, Vector3D> _jumpPointByPlanetCache = new Dictionary<long, Vector3D>();
        readonly Dictionary<long, JumpPointGpsEntry> _jumpPointGpsEntries = new Dictionary<long, JumpPointGpsEntry>();
        long _jumpPointCacheFrame = -1;
        static readonly TimeSpan JumpPointGpsTtl = TimeSpan.FromSeconds(60);

        struct JumpPointGpsEntry
        {
            public IMyGps Gps;
            public long LastPublishedFrame;
        }

        /// <summary>
        /// Logic attached to <see cref="grid"/>
        /// </summary>
        /// <param name="grid"></param>
        public GridLogic(IMyCubeGrid grid)
        {
            Grid = grid;
            _gridGroupResolver = new GridGroupLogic(this);
            _clock = new Random().Next(DELAY);
            // Initial Randomization so not every single grid ticks on the same time
        }

        internal GridGroupLogic GetLocalGridGroupResolver()
        {
            if (_gridGroupResolver == null || _gridGroupResolver.Owner != this)
                _gridGroupResolver = new GridGroupLogic(this);
            return _gridGroupResolver;
        }

        internal void SetGridGroupResolver(GridGroupLogic resolver)
        {
            if (resolver != null)
                _gridGroupResolver = resolver;
        }

        public void MarkRequested()
        {
            _ticksSinceRequested = 0;
        }

        /// <summary>
        /// Update Grid component after specific <see cref="DELAY"/>, Called every tick
        /// </summary>
        public void Update()
        {
            if (_ticksSinceRequested < int.MaxValue)
                _ticksSinceRequested++;

            if (_ticksSinceRequested > REQUEST_TTL_TICKS)
            {
                if (_refreshUpdater != null)
                {
                    _refreshUpdater.Dispose();
                    _refreshUpdater = null;
                    _refreshQueued = false;
                }
                return;
            }

            _clock++;

            try
            {
                // Schedule a refresh cycle periodically, but keep current data until the new snapshot is ready.
                if (_clock % DELAY == 0)
                {
                    InvalidateItemCaches();
                    StartRefresh(force: true);
                }

                AdvanceRefreshUpdater();
            }
            catch (Exception e)
            {
                ErrorHandlerHelper.LogError(e, this);
            }
        }

        void InvalidateItemCaches()
        {
            _compCache.Clear();
            _queryCache.Clear();
            _refineryInputQueryCache.Clear();
            _refineryOutputQueryCache.Clear();
        }

        void StartRefresh(bool force = false)
        {
            if (_refreshUpdater != null)
            {
                if (force)
                    _refreshQueued = true;
                return;
            }

            if (!force && _blocks.Count > 0)
                return;

            _currentRefreshBatchSize = Math.Max(1, _nextRefreshBatchSize);
            _currentRefreshIterations = 0;
            _currentRefreshProcessed = 0;
            _refreshUpdater = RefreshInventoriesCoroutine().GetEnumerator();
            _refreshQueued = false;
        }

        void AdvanceRefreshUpdater()
        {
            if (_refreshUpdater == null)
                return;

            bool hasMore;
            try
            {
                _currentRefreshIterations++;
                hasMore = _refreshUpdater.MoveNext();
            }
            catch (Exception e)
            {
                ErrorHandlerHelper.LogError(e, this);
                _refreshUpdater.Dispose();
                _refreshUpdater = null;
                _refreshQueued = false;
                return;
            }

            if (hasMore)
                return;

            _refreshUpdater.Dispose();
            _refreshUpdater = null;
            FinalizeRefreshEstimate();

            if (_refreshQueued)
                StartRefresh(force: true);
        }

        void FinalizeRefreshEstimate()
        {
            _lastRefreshIterations = _currentRefreshIterations;
            _lastRefreshProcessed = _currentRefreshProcessed;

            if (_lastRefreshProcessed <= 0)
                return;

            // Estimate batch size so the next refresh tends to complete in about TARGET_REFRESH_TICKS updates.
            _nextRefreshBatchSize = Math.Max(1,
                (int)Math.Ceiling(_lastRefreshProcessed / (double)TARGET_REFRESH_TICKS));
        }

        /// <summary>
        /// Collect items from <see cref="blocks"/> with specific <see cref="categories"/>> or specific <see cref="idWhiteList"/> and add to <see cref="dictionary"/>
        /// </summary>
        /// <param name="blocks">Blocks to collect from</param>
        /// <param name="dictionary">Dictionary to store item Type/Ammount</param>
        /// <param name="categories">Suffix of the item to be collected</param>
        /// <param name="idWhiteList">Items to be collected</param>
        void AggregateItems(List<IMyTerminalBlock> blocks, Dictionary<MyItemType, double> dictionary,
            string[] categories, MyDefinitionId[] idWhiteList)
        {
            dictionary.Clear();

            for (int b = 0; b < blocks.Count; b++)
            {
                var tb = blocks[b];

                if (!tb.HasInventory) 
                    continue;

                int invCount = tb.InventoryCount;
                for (int i = 0; i < invCount; i++)
                {
                    var inv = tb.GetInventory(i);
                    if (inv == null) continue;

                    var items = new List<IngameItem>();
                    inv.GetItems(items);
                    for (int k = 0; k < items.Count; k++)
                    {
                        var it = items[k];

                        var typeIdStr = it.Type.TypeId;

                        var filter = categories.Length > 0 || idWhiteList.Length > 0;

                        if (filter)
                        {
                            var match =
                                categories.Any(category =>
                                    typeIdStr.EndsWith(category, StringComparison.OrdinalIgnoreCase)) ||
                                idWhiteList.Any(definition => definition.Equals(it.Type));

                            if (!match)
                                continue;
                        }


                        MyItemType type = it.Type;

                        double amount = (double)it.Amount;
                        if (amount <= 0) continue;

                        double acc;
                        if (dictionary.TryGetValue(type, out acc)) dictionary[type] = acc + amount;
                        else dictionary[type] = amount;
                    }
                }
            }
        }


        public Dictionary<MyItemType, double> GetItems(ScreenConfigWithItems config, IMyTerminalBlock referenceBlock, string[] types = null)
        {
            try
            {
                GridLinkTypeEnum linkType = config.GridLinkType;
                SearchQueryToken queryToken = SearchQueryToken.GetToken(config);
                Dictionary<MyItemType, double> dictionary;
                if (!_queryCache.TryGetValue(queryToken, out dictionary))
                {
                    dictionary = new Dictionary<MyItemType, double>();

                    List<IMyTerminalBlock> blocks =
                        config.SelectedBlocks.Length == 0 && config.SelectedGroups.Length == 0
                            ? GetInventories(linkType)
                            : new List<IMyTerminalBlock>();

                    blocks.AddRange(config.SelectedBlocks.Select(id => MyAPIGateway.Entities.GetEntityById(id))
                        .Select(entity => entity as IMyTerminalBlock)
                        .Where(block =>
                            block != null && block.HasInventory &&
                            IsBlockInGridLinkScope(block, referenceBlock, linkType)));

                    if (config.SelectedGroups.Any())
                    {
                        List<IMyTerminalBlock> blockFromGroups = new List<IMyTerminalBlock>();
                        foreach (var groupName in config.SelectedGroups)
                        {
                            blockFromGroups.Clear();
                            GridTerminalSystem.GetBlockGroupWithName(groupName)?
                                .GetBlocks(blockFromGroups, b => b.HasInventory &&
                                                                 b.GetUserRelationToOwner(referenceBlock.OwnerId)
                                                                 <= MyRelationsBetweenPlayerAndBlock.FactionShare &&
                                                                 IsBlockInGridLinkScope(b, referenceBlock, linkType) &&
                                                                 !blocks.Contains(b));
                            blocks.AddRange(blockFromGroups);
                        }
                    }

                    AggregateItems(blocks, dictionary, types ?? config.SelectedCategories, config.SelectedItems);

                    _queryCache[queryToken] = dictionary;
                }

                return dictionary;
            }
            catch (Exception e)
            {
                ErrorHandlerHelper.LogError(e, this);
                return new Dictionary<MyItemType, double>();
            }
        }

        public List<IMyTerminalBlock> GetInventories()
        {
            RefreshIfNeeded();
            return _invBlocks;
        }

        public List<IMyTerminalBlock> GetInventories(GridLinkTypeEnum linkType)
        {
            var terminals = GetTerminalBlocks<IMyTerminalBlock>(linkType);
            var inventories = new List<IMyTerminalBlock>();
            if (terminals == null)
                return inventories;

            for (int i = 0; i < terminals.Count; i++)
            {
                var block = terminals[i];
                if (block != null && block.HasInventory)
                    inventories.Add(block);
            }

            return inventories;
        }

        bool IsBlockInGridLinkScope(IMyTerminalBlock block, IMyTerminalBlock referenceBlock, GridLinkTypeEnum linkType)
        {
            if (block == null || referenceBlock == null || block.CubeGrid == null || referenceBlock.CubeGrid == null)
                return false;

            if (block.CubeGrid.EntityId == referenceBlock.CubeGrid.EntityId)
                return true;

            var terminals = GetTerminalBlocks<IMyTerminalBlock>(linkType);
            if (terminals == null)
                return false;

            for (int i = 0; i < terminals.Count; i++)
            {
                var terminal = terminals[i];
                if (terminal != null && terminal.EntityId == block.EntityId)
                    return true;
            }

            return false;
        }

        public void RefreshIfNeeded()
        {
            StartRefresh();
        }

        public static bool EnsureAssemblerBlueprintDatabase(IMyAssembler assembler)
        {
            if (assembler == null || MyDefinitionManager.Static == null)
                return false;

            var assemblerSubtype = GetAssemblerSubtype(assembler);
            if (string.IsNullOrEmpty(assemblerSubtype))
                return false;

            lock (AssemblerBlueprintDatabaseLock)
            {
                EnsureBlueprintResultDatabaseNoLock();

                if (CraftableBlueprintsByAssemblerSubtype.ContainsKey(assemblerSubtype))
                    return false;

                var craftableBlueprints = new HashSet<MyDefinitionId>();
                foreach (var blueprint in MyDefinitionManager.Static.GetBlueprintDefinitions())
                {
                    if (blueprint == null || !CanAssemblerUseBlueprint(assembler, blueprint))
                        continue;

                    craftableBlueprints.Add(blueprint.Id);
                    AddToSet(AssemblerSubtypesByCraftableBlueprint, blueprint.Id, assemblerSubtype);
                }

                CraftableBlueprintsByAssemblerSubtype[assemblerSubtype] = craftableBlueprints;
                return true;
            }
        }

        static void EnsureBlueprintResultDatabaseNoLock()
        {
            if (_blueprintResultDatabaseInitialized || MyDefinitionManager.Static == null)
                return;

            foreach (var blueprint in MyDefinitionManager.Static.GetBlueprintDefinitions())
            {
                if (blueprint == null || CreatedItemsByBlueprint.ContainsKey(blueprint.Id))
                    continue;

                var createdItems = new HashSet<MyDefinitionId>();
                var results = blueprint.Results;
                if (results != null)
                {
                    for (int i = 0; i < results.Length; i++)
                    {
                        var itemId = results[i].Id;
                        createdItems.Add(itemId);
                        AddToSet(BlueprintsByCreatedItem, itemId, blueprint.Id);
                    }
                }

                if (blueprint.IsPrimary && results?.Length >= 1) 
                    PrimaryBlueprintByCreatedItem[results.First().Id] = blueprint;

                CreatedItemsByBlueprint[blueprint.Id] = createdItems;
            }

            _blueprintResultDatabaseInitialized = true;
        }

        static bool CanAssemblerUseBlueprint(IMyAssembler assembler, MyBlueprintDefinitionBase blueprint)
        {
            try
            {
                return assembler.CanUseBlueprint(blueprint.Id);
            }
            catch (Exception e)
            {
                ErrorHandlerHelper.LogError(e, typeof(GridLogic));
                return false;
            }
        }

        public static string GetAssemblerSubtype(IMyAssembler assembler)
        {
            if (assembler == null)
                return string.Empty;

            var definitionId = assembler.BlockDefinition;
            var subtype = definitionId.SubtypeName;
            return string.IsNullOrEmpty(subtype) ? definitionId.ToString() : subtype;
        }

        static void AddToSet<TKey, TValue>(Dictionary<TKey, HashSet<TValue>> dictionary, TKey key, TValue value)
        {
            HashSet<TValue> values;
            if (!dictionary.TryGetValue(key, out values))
            {
                values = new HashSet<TValue>();
                dictionary[key] = values;
            }

            values.Add(value);
        }

        IEnumerable<bool> RefreshInventoriesCoroutine()
        {
            _nextBlocks.Clear();
            _nextInvBlocks.Clear();
            _nextLasers.Clear();
            _nextRadio.Clear();
            _nextBeacons.Clear();
            _nextBatteries.Clear();
            _nextJumpDrives.Clear();
            _nextAssemblers.Clear();
            _nextTerminalBlocks.Clear();
            _nextCargoContainers.Clear();
            _nextGasTanks.Clear();
            _nextPowerProducers.Clear();
            _nextFarmPlots.Clear();

            Grid.GetBlocks(_nextBlocks, a => a.FatBlock is IMyTerminalBlock);

            int processed = 0;
            for (int i = 0; i < _nextBlocks.Count; i++)
            {
                var block = _nextBlocks[i].FatBlock as IMyTerminalBlock;
                if (block == null)
                    continue;

                _nextTerminalBlocks.Add(block);

                var cargo = block as IMyCargoContainer;
                if (cargo != null)
                    _nextCargoContainers.Add(cargo);

                var gasTank = block as IMyGasTank;
                if (gasTank != null)
                    _nextGasTanks.Add(gasTank);

                var producer = block as IMyPowerProducer;
                if (producer != null)
                    _nextPowerProducers.Add(producer);

                var antenna = block as IMyRadioAntenna;
                if (antenna != null)
                    _nextRadio.Add(antenna);

                var beacon = block as IMyBeacon;
                if (beacon != null)
                    _nextBeacons.Add(beacon);

                var laser = block as IMyLaserAntenna;
                if (laser != null)
                    _nextLasers.Add(laser);

                var battery = block as IMyBatteryBlock;
                if (battery != null)
                    _nextBatteries.Add(battery);

                var jumpDrive = block as IMyJumpDrive;
                if (jumpDrive != null)
                    _nextJumpDrives.Add(jumpDrive);

                var assembler = block as IMyAssembler;
                if (assembler != null)
                {
                    _nextAssemblers.Add(assembler);
                    EnsureAssemblerBlueprintDatabase(assembler);
                }

                var farmPlotBlock = block as IMyFunctionalBlock;
                if (farmPlotBlock != null)
                {
                    if (KnowFarmSubtypes.Contains(farmPlotBlock.BlockDefinition.SubtypeName) || KnowSubtypes.Add(farmPlotBlock.BlockDefinition.SubtypeName))
                    {
                        IMyFarmPlotLogic planterComponent = null;
                        IMyResourceStorageComponent storageComponent = null;
                        
                        foreach (var component in block.Components)
                        {
                            if (planterComponent == null)
                                planterComponent = component as IMyFarmPlotLogic;

                            if (storageComponent == null)
                                storageComponent = component as IMyResourceStorageComponent;

                            if (planterComponent == null || storageComponent == null)
                                continue;

                            KnowFarmSubtypes.Add(farmPlotBlock.BlockDefinition.SubtypeName);
                            _nextFarmPlots.Add(new FarmPlotEntry(farmPlotBlock, planterComponent, storageComponent));
                            break;
                        }
                    }
                }

                if (block.HasInventory && block.InventoryCount != 0)
                    _nextInvBlocks.Add(block);

                processed++;
                _currentRefreshProcessed++;
                if (processed >= _currentRefreshBatchSize)
                {
                    processed = 0;
                    yield return true;
                }
            }

            // Atomically swap the visible snapshot once fully built.
            SwapBuffer(ref _radio, ref _nextRadio);
            SwapBuffer(ref _lasers, ref _nextLasers);
            SwapBuffer(ref _blocks, ref _nextBlocks);
            SwapBuffer(ref _beacons, ref _nextBeacons);
            SwapBuffer(ref _gasTanks, ref _nextGasTanks);
            SwapBuffer(ref _invBlocks, ref _nextInvBlocks);
            SwapBuffer(ref _farmPlots, ref _nextFarmPlots);
            SwapBuffer(ref _batteries, ref _nextBatteries);
            SwapBuffer(ref _jumpDrives, ref _nextJumpDrives);
            SwapBuffer(ref _assemblers, ref _nextAssemblers);
            SwapBuffer(ref _terminalBlocks, ref _nextTerminalBlocks);
            SwapBuffer(ref _powerProducers, ref _nextPowerProducers);
            SwapBuffer(ref _cargoContainers, ref _nextCargoContainers);


        }

        internal List<T> GetTerminalBlocksInternal<T>() where T : IMyTerminalBlock
        {
            RefreshIfNeeded();
            switch (typeof(T).Name)
            {
                case nameof(IMyTerminalBlock):
                    return _terminalBlocks as List<T>;
                case nameof(IMyCargoContainer):
                    return _cargoContainers as List<T>;
                case nameof(IMyGasTank):
                    return _gasTanks as List<T>;
                case nameof(IMyPowerProducer):
                    return _powerProducers as List<T>;
                case nameof(IMyLaserAntenna):
                    return _lasers as List<T>;
                case nameof(IMyRadioAntenna):
                    return _radio as List<T>;
                case nameof(IMyBeacon):
                    return _beacons as List<T>;
                case nameof(IMyBatteryBlock):
                    return _batteries as List<T>;
                case nameof(IMyJumpDrive):
                    return _jumpDrives as List<T>;
                case nameof(IMyAssembler):
                    return _assemblers as List<T>;
            }

            throw new NotImplementedException(typeof(T).Name);
        }

        public List<T> GetTerminalBlocks<T>(GridLinkTypeEnum linkType = (GridLinkTypeEnum)(-1)) where T : IMyTerminalBlock
        {
            if(linkType == (GridLinkTypeEnum)(-1))
                return GetTerminalBlocksInternal<T>();
            
            if (linkType != GridLinkTypeEnum.Physical && linkType != GridLinkTypeEnum.Mechanical)
                throw new NotImplementedException(typeof(T).Name);

            var resolver = GridGroupLogic.ResolveFor(this);
            if (resolver == null)
                return GetTerminalBlocksInternal<T>();

            return resolver.GetTerminalBlocks<T>(this, linkType);
        }

        public List<FarmPlotEntry> GetFarmPlots()
        {
            RefreshIfNeeded();
            return _farmPlots;
        }

        public bool TryGetPlanetJumpPoint(
            long planetId,
            string planetName,
            Vector3D planetCenter,
            double planetRadiusMeters,
            double gravityRangeMeters,
            out Vector3D jumpPoint,
            bool publish = true)
        {
            jumpPoint = Vector3D.Zero;
            if (Grid == null)
                return false;

            var jumpDrives = GetTerminalBlocksInternal<IMyJumpDrive>();
            if (jumpDrives == null || jumpDrives.Count == 0)
                return false;

            long frame = MyAPIGateway.Session != null ? MyAPIGateway.Session.GameplayFrameCounter : -1;
            if (_jumpPointCacheFrame != frame)
            {
                _jumpPointByPlanetCache.Clear();
                _jumpPointCacheFrame = frame;
            }

            JumpPointGpsEntry gpsEntry;
            if (!_jumpPointGpsEntries.TryGetValue(planetId, out gpsEntry))
                gpsEntry = new JumpPointGpsEntry();
            _jumpPointGpsEntries[planetId] = gpsEntry;

            if (_jumpPointByPlanetCache.TryGetValue(planetId, out jumpPoint))
                return true;

            var gridPos = Grid.GetPosition();
            var dir = gridPos - planetCenter;
            if (dir.LengthSquared() <= 1e-6)
                dir = Vector3D.Forward;
            else
                dir.Normalize();

            var offsetMeters = Math.Max(0d, planetRadiusMeters + gravityRangeMeters + 10d);
            jumpPoint = planetCenter + dir * offsetMeters;
            _jumpPointByPlanetCache[planetId] = jumpPoint;
            
            if(publish)
                PublishJumpPointGps(planetId, planetName, jumpPoint, frame);
            return true;
        }

        void PublishJumpPointGps(long planetId, string planetName, Vector3D jumpPoint, long frame)
        {
            if (frame < 0 || MyAPIGateway.Session == null || MyAPIGateway.Session.GPS == null)
                return;

            JumpPointGpsEntry entry;
            if (!_jumpPointGpsEntries.TryGetValue(planetId, out entry))
                return;

            if (frame - entry.LastPublishedFrame < 60)
                return;

            var gps = entry.Gps;
            var discardAt = MyAPIGateway.Session.ElapsedPlayTime + JumpPointGpsTtl;
            if (gps == null || (gps.DiscardAt.HasValue && gps.DiscardAt.Value <= MyAPIGateway.Session.ElapsedPlayTime))
            {
                var gpsName = BuildJumpPointGpsName(planetName);
                gps = MyAPIGateway.Session.GPS.Create(gpsName, string.Empty, jumpPoint, true, true);
                if (gps == null)
                    return;
                gps.DiscardAt = discardAt;
                MyAPIGateway.Session.GPS.AddLocalGps(gps);
                entry.Gps = gps;
            }
            else
            {
                gps.Coords = jumpPoint;
                gps.DiscardAt = discardAt;
            }

            entry.LastPublishedFrame = frame;
            _jumpPointGpsEntries[planetId] = entry;
        }

        string BuildJumpPointGpsName(string planetName)
        {
            var gridToken = GetGridNameToken();
            var planetToken = string.IsNullOrWhiteSpace(planetName)
                ? "Unknown"
                : FormatingHelper.TrimName(SanitizeGpsNameToken(planetName));
            return "JumpPoint_" + gridToken + "_" + planetToken;
        }

        string GetGridNameToken()
        {
            var gridName = Grid?.CustomName;
            if (string.IsNullOrWhiteSpace(gridName))
                return "Unknown";

            var token = FormatingHelper.TrimName(SanitizeGpsNameToken(gridName));
            if (token.Length == 0)
                token = "Unknown";
            return token;
        }

        static string SanitizeGpsNameToken(string raw)
        {
            if (string.IsNullOrEmpty(raw))
                return string.Empty;

            var chars = new List<char>(raw.Length);
            for (int i = 0; i < raw.Length; i++)
            {
                var c = raw[i];
                if (char.IsLetterOrDigit(c) || c == '_' || c == '-')
                    chars.Add(c);
                else if (char.IsWhiteSpace(c))
                    chars.Add('_');
            }

            return new string(chars.ToArray());
        }

        /// <summary>
        /// Collects items from a single inventory slot of refineries, respecting block/group selection.
        /// </summary>
        /// <param name="inventoryIndex">0 = input (ores), 1 = output (ingots)</param>
        /// <param name="config">Screen config used to filter selected blocks/groups.</param>
        /// <param name="referenceBlock">Lcd block used for ownership/grid-group checks.</param>
        /// <returns>Item type/amount dictionary for the requested slot; empty if no refineries found.</returns>
        public Dictionary<MyItemType, double> GetRefineryItems(int inventoryIndex, ScreenConfigWithBlocks config, IMyTerminalBlock referenceBlock)
        {
            if (inventoryIndex != 0 && inventoryIndex != 1)
                return new Dictionary<MyItemType, double>();

            try
            {
                SearchQueryToken token      = SearchQueryToken.GetToken(config);
                var              queryCache = inventoryIndex == 0 ? _refineryInputQueryCache : _refineryOutputQueryCache;

                Dictionary<MyItemType, double> cache;
                if (queryCache.TryGetValue(token, out cache))
                    return cache;

                cache = new Dictionary<MyItemType, double>();

                // Build refinery block list, respecting SelectedBlocks / SelectedGroups
                List<IMyTerminalBlock> blocks;
                if (config.SelectedBlocks.Length == 0 && config.SelectedGroups.Length == 0)
                {
                    var all = GetInventories(config.GridLinkType);
                    blocks = new List<IMyTerminalBlock>(all.Count);
                    for (int i = 0; i < all.Count; i++)
                        if (all[i] is IMyRefinery)
                            blocks.Add(all[i]);
                }
                else
                {
                    blocks = new List<IMyTerminalBlock>();
                    blocks.AddRange(config.SelectedBlocks
                        .Select(id => MyAPIGateway.Entities.GetEntityById(id) as IMyTerminalBlock)
                        .Where(b => (b is IMyRefinery || b is IMyAssembler) &&
                                    IsBlockInGridLinkScope(b, referenceBlock, config.GridLinkType)));

                    if (config.SelectedGroups.Length > 0)
                    {
                        var groupBlocks = new List<IMyTerminalBlock>();
                        foreach (var groupName in config.SelectedGroups)
                        {
                            groupBlocks.Clear();
                            GridTerminalSystem.GetBlockGroupWithName(groupName)?
                                .GetBlocks(groupBlocks, b => (b is IMyRefinery || b is IMyAssembler) &&
                                                             b.GetUserRelationToOwner(referenceBlock.OwnerId)
                                                             <= MyRelationsBetweenPlayerAndBlock.FactionShare &&
                                                             IsBlockInGridLinkScope(b, referenceBlock, config.GridLinkType) &&
                                                             !blocks.Contains(b));
                            blocks.AddRange(groupBlocks);
                        }
                    }
                }

                var items = new List<IngameItem>();
                for (int b = 0; b < blocks.Count; b++)
                {
                    var tb = blocks[b];
                    if (inventoryIndex >= tb.InventoryCount) continue;
                    var inv = tb.GetInventory(inventoryIndex);
                    if (inv == null) continue;
                    items.Clear();
                    inv.GetItems(items);
                    for (int k = 0; k < items.Count; k++)
                    {
                        var    it     = items[k];
                        double amount = (double)it.Amount;
                        if (amount <= 0) continue;
                        MyItemType type = it.Type;
                        double acc;
                        if (cache.TryGetValue(type, out acc)) cache[type] = acc + amount;
                        else                                   cache[type] = amount;
                    }
                }

                queryCache[token] = cache;
                return cache;
            }
            catch (Exception e)
            {
                ErrorHandlerHelper.LogError(e, this);
                return new Dictionary<MyItemType, double>();
            }
        }

        static void SwapBuffer<T>(ref T active, ref T next) where T : class, IList
        {
            var old = active;
            active = next;
            next = old;
            next?.Clear();
        }
    }
}
