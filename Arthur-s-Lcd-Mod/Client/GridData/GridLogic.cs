using LcdMod.Common.Config.Components;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using LcdMod.Client.Helpers;
using LcdMod.Client.Terminal.Actions;
using LcdMod.Common.Helpers;
using LcdMod.Common.Networking;
using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.Game.ModAPI.Ingame;
using VRageMath;
using IMyCubeBlock = VRage.Game.ModAPI.IMyCubeBlock;
using IMyCubeGrid = VRage.Game.ModAPI.IMyCubeGrid;
using IMyFarmPlotLogic = Sandbox.ModAPI.IMyFarmPlotLogic;
using IMyFunctionalBlock = Sandbox.ModAPI.IMyFunctionalBlock;
using IMySlimBlock = VRage.Game.ModAPI.IMySlimBlock;
using IngameItem = VRage.Game.ModAPI.Ingame.MyInventoryItem;
using NotImplementedException = LcdMod.Common.NotImplementedException;
namespace LcdMod.Client.GridData
{
    /// <summary>
    ///     Logic attached to <see cref="Grid" />
    /// </summary>
    public class GridLogic
    {
        private const int DELAY = 120;
        private const int REQUEST_TTL_TICKS = 120;
        private const int TARGET_REFRESH_TICKS = 119;
        private const int REFRESH_BATCH_SIZE = 128;
        private static readonly object AssemblerBlueprintDatabaseLock = new object();
        private static bool _blueprintResultDatabaseInitialized;

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

        public static readonly HashSet<string> KnowSubtypes = new HashSet<string>();
        private static readonly HashSet<string> KnowFarmSubtypes = new HashSet<string>();

        private static readonly string[] IngotTypeFilter = { "Ingot" };
        private static readonly TimeSpan JumpPointGpsTtl = TimeSpan.FromSeconds(60);

        private ItemsComponent _itemsComponent;
        private BlocksComponent _blocksComponent;
        private PowerComponent _powerComponent;
        private GridRoomEnvironmentComponent _roomEnvironmentComponent;
        private TerrainSolarForecastComponent _terrainSolarForecastComponent;
#if EXPERIMENTAL
        private readonly Dictionary<MediaPlayerPartitionKey, GridMediaPlayer> _mediaPlayers =
            new Dictionary<MediaPlayerPartitionKey, GridMediaPlayer>();
#endif

        private readonly Dictionary<MyItemType, double> _compCache = new Dictionary<MyItemType, double>();

        private readonly Dictionary<MyItemType, double> _ingotCache = new Dictionary<MyItemType, double>();

        private readonly Dictionary<SearchQueryToken, Dictionary<MyItemType, double>> _ingotQueryCache =
            new Dictionary<SearchQueryToken, Dictionary<MyItemType, double>>();

        private readonly Dictionary<long, Vector3D> _jumpPointByPlanetCache = new Dictionary<long, Vector3D>();

        private readonly Dictionary<long, JumpPointGpsEntry> _jumpPointGpsEntries =
            new Dictionary<long, JumpPointGpsEntry>();

        private readonly Dictionary<SearchQueryToken, List<ProductionBlockItems>> _productionByBlockCache =
            new Dictionary<SearchQueryToken, List<ProductionBlockItems>>();

        private readonly Dictionary<SearchQueryToken, Dictionary<MyItemType, double>> _queryCache =
            new Dictionary<SearchQueryToken, Dictionary<MyItemType, double>>();

        private readonly Dictionary<SearchQueryToken, ItemSnapshot> _itemSnapshots =
            new Dictionary<SearchQueryToken, ItemSnapshot>();


        public readonly IMyCubeGrid Grid;
        private List<IMyAssembler> _assemblers = new List<IMyAssembler>();
        private List<IMyBatteryBlock> _batteries = new List<IMyBatteryBlock>();
        private List<IMyBeacon> _beacons = new List<IMyBeacon>();
        private List<IMySlimBlock> _blocks = new List<IMySlimBlock>();
        private List<IMyCargoContainer> _cargoContainers = new List<IMyCargoContainer>();
        private long _clock;
        private int _currentRefreshIterations;
        private int _currentRefreshProcessed;
        private List<FarmPlotEntry> _farmPlots = new List<FarmPlotEntry>();
        private List<IMyGasTank> _gasTanks = new List<IMyGasTank>();
        private GridGroupLogic _gridGroupResolver;
        private List<IMyTerminalBlock> _invBlocks = new List<IMyTerminalBlock>();
        private List<IMyJumpDrive> _jumpDrives = new List<IMyJumpDrive>();
        private long _jumpPointCacheFrame = -1;

        private List<IMyLaserAntenna> _lasers = new List<IMyLaserAntenna>();
        private List<IMyAssembler> _nextAssemblers = new List<IMyAssembler>();
        private List<IMyBatteryBlock> _nextBatteries = new List<IMyBatteryBlock>();
        private List<IMyBeacon> _nextBeacons = new List<IMyBeacon>();
        private List<IMySlimBlock> _nextBlocks = new List<IMySlimBlock>();
        private List<IMyCargoContainer> _nextCargoContainers = new List<IMyCargoContainer>();
        private List<FarmPlotEntry> _nextFarmPlots = new List<FarmPlotEntry>();
        private List<IMyGasTank> _nextGasTanks = new List<IMyGasTank>();
        private List<IMyTerminalBlock> _nextInvBlocks = new List<IMyTerminalBlock>();
        private List<IMyJumpDrive> _nextJumpDrives = new List<IMyJumpDrive>();
        private List<IMyLaserAntenna> _nextLasers = new List<IMyLaserAntenna>();
        private List<IMyPowerProducer> _nextPowerProducers = new List<IMyPowerProducer>();
        private List<IMyRadioAntenna> _nextRadio = new List<IMyRadioAntenna>();
        private List<IMyTerminalBlock> _nextTerminalBlocks = new List<IMyTerminalBlock>();
        private List<IMyPowerProducer> _powerProducers = new List<IMyPowerProducer>();
        private List<IMyRadioAntenna> _radio = new List<IMyRadioAntenna>();
        private bool _refreshQueued;
        private IEnumerator<bool> _refreshUpdater;
        private List<IMyTerminalBlock> _terminalBlocks = new List<IMyTerminalBlock>();
        private int _ticksSinceRequested = int.MaxValue;

        /// <summary>
        ///     Logic attached to <see cref="grid" />
        /// </summary>
        /// <param name="grid"></param>
        public GridLogic(IMyCubeGrid grid)
        {
            Grid = grid;
            _gridGroupResolver = new GridGroupLogic(this);
            _clock = new Random().Next(DELAY);
            // Initial Randomization so not every single grid ticks on the same time
        }

        public int LastRefreshIterations { get; private set; }

        public int LastRefreshProcessed { get; private set; }

        public int EstimatedNextRefreshBatchSize { get; private set; } = REFRESH_BATCH_SIZE;

        public int CurrentRefreshBatchSize { get; private set; } = REFRESH_BATCH_SIZE;

        public bool IsRefreshRunning => _refreshUpdater != null;
        public bool IsSleeping => _ticksSinceRequested > REQUEST_TTL_TICKS;

        public ItemsComponent Items => _itemsComponent ?? (_itemsComponent = new ItemsComponent(this));

        public BlocksComponent Blocks => _blocksComponent ?? (_blocksComponent = new BlocksComponent(this));

        public PowerComponent Power => _powerComponent ?? (_powerComponent = new PowerComponent(this));

        internal GridRoomEnvironmentComponent RoomEnvironment =>
            _roomEnvironmentComponent ?? (_roomEnvironmentComponent = new GridRoomEnvironmentComponent(this));

        internal TerrainSolarForecastComponent TerrainSolarForecast =>
            _terrainSolarForecastComponent ?? (_terrainSolarForecastComponent = new TerrainSolarForecastComponent(this));

#if EXPERIMENTAL
        public GridMediaPlayer MediaPlayer => GetMediaPlayer(0L, 0);

        public GridMediaPlayer GetMediaPlayer(long blockId, int screenIndex)
        {
            if (screenIndex < 0)
                screenIndex = 0;

            var key = new MediaPlayerPartitionKey(blockId, screenIndex);
            GridMediaPlayer player;
            if (!_mediaPlayers.TryGetValue(key, out player))
            {
                player = new GridMediaPlayer();
                _mediaPlayers[key] = player;
            }

            return player;
        }
#endif

        private IMyGridTerminalSystem GridTerminalSystem =>
            MyAPIGateway.TerminalActionsHelper.GetTerminalSystemForGrid(Grid);

        public Dictionary<MyItemType, double> Components
        {
            get { return Items.Components; }
        }

        public Dictionary<MyItemType, double> Ingots
        {
            get { return Items.Ingots; }
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

        public void Unload()
        {
#if EXPERIMENTAL
            foreach (var player in _mediaPlayers.Values)
                player.Unload();
            _mediaPlayers.Clear();
#endif
        }

        /// <summary>
        ///     Update Grid component after specific <see cref="DELAY" />, Called every tick
        /// </summary>
        public void Update()
        {
            if (_ticksSinceRequested < int.MaxValue)
                _ticksSinceRequested++;

#if EXPERIMENTAL
            foreach (var player in _mediaPlayers.Values)
                player.Update();
#endif

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
                    StartRefresh(true);
                }

                AdvanceRefreshUpdater();
            }
            catch (Exception e)
            {
                ErrorHandlerHelper.LogError(e, this);
            }
        }

        private void InvalidateItemCaches()
        {
            _compCache.Clear();
            _ingotCache.Clear();
            _queryCache.Clear();
            _ingotQueryCache.Clear();
            _productionByBlockCache.Clear();
        }

#if EXPERIMENTAL
        private struct MediaPlayerPartitionKey : IEquatable<MediaPlayerPartitionKey>
        {
            readonly long _blockId;
            readonly int _screenIndex;

            public MediaPlayerPartitionKey(long blockId, int screenIndex)
            {
                _blockId = blockId;
                _screenIndex = screenIndex;
            }

            public bool Equals(MediaPlayerPartitionKey other)
            {
                return _blockId == other._blockId && _screenIndex == other._screenIndex;
            }

            public override bool Equals(object obj)
            {
                return obj is MediaPlayerPartitionKey && Equals((MediaPlayerPartitionKey)obj);
            }

            public override int GetHashCode()
            {
                return (_blockId.GetHashCode() * 397) ^ _screenIndex;
            }
        }
#endif

        private void StartRefresh(bool force = false)
        {
            if (_refreshUpdater != null)
            {
                if (force)
                    _refreshQueued = true;
                return;
            }

            if (!force && _blocks.Count > 0)
                return;

            CurrentRefreshBatchSize = Math.Max(1, EstimatedNextRefreshBatchSize);
            _currentRefreshIterations = 0;
            _currentRefreshProcessed = 0;
            _refreshUpdater = RefreshInventoriesCoroutine().GetEnumerator();
            _refreshQueued = false;
        }

        private void AdvanceRefreshUpdater()
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
                StartRefresh(true);
        }

        private void FinalizeRefreshEstimate()
        {
            LastRefreshIterations = _currentRefreshIterations;
            LastRefreshProcessed = _currentRefreshProcessed;

            if (LastRefreshProcessed <= 0)
                return;

            // Estimate batch size so the next refresh tends to complete in about TARGET_REFRESH_TICKS updates.
            EstimatedNextRefreshBatchSize = Math.Max(1,
                (int)Math.Ceiling(LastRefreshProcessed / (double)TARGET_REFRESH_TICKS));
        }

        /// <summary>
        ///     Collect items from <see cref="blocks" /> with specific <see cref="categories" />> or specific
        ///     <see cref="idWhiteList" /> and add to <see cref="dictionary" />
        /// </summary>
        /// <param name="blocks">Blocks to collect from</param>
        /// <param name="dictionary">Dictionary to store item Type/Ammount</param>
        /// <param name="categories">Suffix of the item to be collected</param>
        /// <param name="idWhiteList">Items to be collected</param>
        private void AggregateItems(List<IMyTerminalBlock> blocks, Dictionary<MyItemType, double> dictionary,
            string[] categories, MyDefinitionId[] idWhiteList)
        {
            dictionary.Clear();

            for (var b = 0; b < blocks.Count; b++)
            {
                var tb = blocks[b];

                if (!tb.HasInventory)
                    continue;

                var invCount = tb.InventoryCount;
                for (var i = 0; i < invCount; i++)
                {
                    var inv = tb.GetInventory(i);
                    if (inv == null) continue;

                    var items = new List<IngameItem>();
                    inv.GetItems(items);
                    for (var k = 0; k < items.Count; k++)
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


                        var type = it.Type;

                        var amount = (double)it.Amount;
                        if (amount <= 0) continue;

                        double acc;
                        if (dictionary.TryGetValue(type, out acc)) dictionary[type] = acc + amount;
                        else dictionary[type] = amount;
                    }
                }
            }
        }


        public Dictionary<MyItemType, double> GetItems(BlockSelectionConfigComponent blocksConfig, ItemSelectionConfigComponent itemsConfig, IMyTerminalBlock referenceBlock,
            string[] types = null)
        {
            return Items.GetItems(blocksConfig, itemsConfig, referenceBlock, types);
        }

        public ItemSnapshot GetItemsSnapshot(
            BlockSelectionConfigComponent blocksConfig,
            ItemSelectionConfigComponent itemsConfig,
            IMyTerminalBlock referenceBlock,
            string[] types = null)
        {
            return Items.GetItemsSnapshot(blocksConfig, itemsConfig, referenceBlock, types);
        }

        public Dictionary<MyItemType, double> GetIngots(BlockSelectionConfigComponent blocksConfig, ItemSelectionConfigComponent itemsConfig, IMyTerminalBlock referenceBlock)
        {
            return Items.GetIngots(blocksConfig, itemsConfig, referenceBlock);
        }

        private Dictionary<MyItemType, double> GetItemsCore(BlockSelectionConfigComponent blocksConfig, ItemSelectionConfigComponent itemsConfig,
            IMyTerminalBlock referenceBlock,
            string[] types, Dictionary<SearchQueryToken, Dictionary<MyItemType, double>> cache, bool forceTypes)
        {
            try
            {
                var linkType = (GridLinkTypeEnum)blocksConfig.GridLinkTypeInternal;
                var queryToken = SearchQueryToken.GetToken(blocksConfig, itemsConfig);
                Dictionary<MyItemType, double> dictionary;
                if (!cache.TryGetValue(queryToken, out dictionary))
                {
                    dictionary = new Dictionary<MyItemType, double>();

                    var blocks =
                        blocksConfig.SelectedBlocks.Length == 0 && blocksConfig.SelectedGroups.Length == 0
                            ? GetInventories(linkType)
                            : new List<IMyTerminalBlock>();

                    blocks.AddRange(blocksConfig.SelectedBlocks.Select(id => MyAPIGateway.Entities.GetEntityById(id))
                        .Select(entity => entity as IMyTerminalBlock)
                        .Where(block =>
                            block != null && block.HasInventory &&
                            IsBlockInGridLinkScope(block, referenceBlock, linkType)));

                    if (blocksConfig.SelectedGroups.Any())
                    {
                        var blockFromGroups = new List<IMyTerminalBlock>();
                        foreach (var groupName in blocksConfig.SelectedGroups)
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

                    var aggregateTypes = forceTypes ? types : types ?? itemsConfig.SelectedCategories;
                    AggregateItems(blocks, dictionary, aggregateTypes, itemsConfig.GetSelectedItems());

                    cache[queryToken] = dictionary;
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
            return Blocks.GetInventories();
        }

        public List<IMyTerminalBlock> GetInventories(GridLinkTypeEnum linkType)
        {
            return Blocks.GetInventories(linkType);
        }

        private bool IsBlockInGridLinkScope(IMyTerminalBlock block, IMyTerminalBlock referenceBlock,
            GridLinkTypeEnum linkType)
        {
            if (block == null || referenceBlock == null || block.CubeGrid == null || referenceBlock.CubeGrid == null)
                return false;

            if (block.CubeGrid.EntityId == referenceBlock.CubeGrid.EntityId)
                return true;

            var terminals = GetTerminalBlocks<IMyTerminalBlock>(linkType);
            if (terminals == null)
                return false;

            for (var i = 0; i < terminals.Count; i++)
            {
                var terminal = terminals[i];
                if (terminal != null && terminal.EntityId == block.EntityId)
                    return true;
            }

            return false;
        }

        public void RefreshIfNeeded()
        {
            Blocks.RefreshIfNeeded();
        }

        internal bool TryGetGridRoomEnvironment(IMyCubeBlock block, out GridRoomEnvironmentSample sample)
        {
            return RoomEnvironment.TryGetGridRoomEnvironment(block, out sample);
        }

        internal void ApplyGridRoomEnvironment(PacketSyncGridRoomEnvironment packet)
        {
            RoomEnvironment.ApplyGridRoomEnvironment(packet);
        }

        internal bool TryGetTerrainSolarForecast(
            MyPlanet planet,
            Vector3D rotationAxis,
            out bool hasSunrise,
            out double sunriseHour,
            out bool hasSunset,
            out double sunsetHour)
        {
            return TerrainSolarForecast.TryGetTerrainSolarForecast(
                planet,
                rotationAxis,
                out hasSunrise,
                out sunriseHour,
                out hasSunset,
                out sunsetHour);
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

        public static void EnsureBlueprintResultDatabase()
        {
            lock (AssemblerBlueprintDatabaseLock)
            {
                EnsureBlueprintResultDatabaseNoLock();
            }
        }

        private static void EnsureBlueprintResultDatabaseNoLock()
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
                    for (var i = 0; i < results.Length; i++)
                    {
                        var itemId = results[i].Id;
                        createdItems.Add(itemId);
                        AddToSet(BlueprintsByCreatedItem, itemId, blueprint.Id);
                    }

                // Map each produced item to the blueprint that makes it. A primary blueprint always
                // wins; a non-primary one only fills the gap when an item has no primary blueprint at
                // all. Vanilla's ReactorComponent ships without <IsPrimary>, so without this fallback
                // reactor components never expand and their gravel/iron/silver vanish from the
                // "ingots needed" estimate (the gravel is used by nothing else, so it disappears).
                if (results != null && results.Length >= 1)
                {
                    var primaryResultId = results.First().Id;
                    if (blueprint.IsPrimary || !PrimaryBlueprintByCreatedItem.ContainsKey(primaryResultId))
                        PrimaryBlueprintByCreatedItem[primaryResultId] = blueprint;
                }

                CreatedItemsByBlueprint[blueprint.Id] = createdItems;
            }

            _blueprintResultDatabaseInitialized = true;
        }

        private static bool CanAssemblerUseBlueprint(IMyAssembler assembler, MyBlueprintDefinitionBase blueprint)
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

        private static void AddToSet<TKey, TValue>(Dictionary<TKey, HashSet<TValue>> dictionary, TKey key, TValue value)
        {
            HashSet<TValue> values;
            if (!dictionary.TryGetValue(key, out values))
            {
                values = new HashSet<TValue>();
                dictionary[key] = values;
            }

            values.Add(value);
        }

        private IEnumerable<bool> RefreshInventoriesCoroutine()
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

            var processed = 0;
            for (var i = 0; i < _nextBlocks.Count; i++)
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

                var newBlock = KnowSubtypes.Add(block.BlockDefinition.SubtypeName);

                var myFunctionalBlock = block as IMyFunctionalBlock;

#if EXPERIMENTAL
                if (newBlock && myFunctionalBlock != null) ActionHelper.RegisterNewBlock(myFunctionalBlock);
#endif


                if (myFunctionalBlock != null)
                    if (KnowFarmSubtypes.Contains(myFunctionalBlock.BlockDefinition.SubtypeName) || newBlock)
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

                            KnowFarmSubtypes.Add(myFunctionalBlock.BlockDefinition.SubtypeName);
                            _nextFarmPlots.Add(new FarmPlotEntry(myFunctionalBlock, planterComponent,
                                storageComponent));
                            break;
                        }
                    }

                if (block.HasInventory && block.InventoryCount != 0)
                    _nextInvBlocks.Add(block);

                processed++;
                _currentRefreshProcessed++;
                if (processed >= CurrentRefreshBatchSize)
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
            return Blocks.GetTerminalBlocksInternal<T>();
        }

        public List<T> GetTerminalBlocks<T>(GridLinkTypeEnum linkType = (GridLinkTypeEnum)(-1))
            where T : IMyTerminalBlock
        {
            return Blocks.GetTerminalBlocks<T>(linkType);
        }

        public List<FarmPlotEntry> GetFarmPlots()
        {
            return Blocks.GetFarmPlots();
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

            if (publish)
                PublishJumpPointGps(planetId, planetName, jumpPoint, frame);
            return true;
        }

        private void PublishJumpPointGps(long planetId, string planetName, Vector3D jumpPoint, long frame)
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

        private string BuildJumpPointGpsName(string planetName)
        {
            var gridToken = GetGridNameToken();
            var planetToken = string.IsNullOrWhiteSpace(planetName)
                ? "Unknown"
                : FormatingHelper.TrimName(SanitizeGpsNameToken(planetName));
            return "JumpPoint_" + gridToken + "_" + planetToken;
        }

        private string GetGridNameToken()
        {
            var gridName = Grid?.CustomName;
            if (string.IsNullOrWhiteSpace(gridName))
                return "Unknown";

            var token = FormatingHelper.TrimName(SanitizeGpsNameToken(gridName));
            if (token.Length == 0)
                token = "Unknown";
            return token;
        }

        private static string SanitizeGpsNameToken(string raw)
        {
            if (string.IsNullOrEmpty(raw))
                return string.Empty;

            var chars = new List<char>(raw.Length);
            for (var i = 0; i < raw.Length; i++)
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
        ///     Per-block input/output snapshot of the grid's refineries and assemblers, respecting
        ///     block/group selection. Cached by query token and rebuilt on the batched refresh cycle.
        /// </summary>
        public List<ProductionBlockItems> GetProductionBlockItems(BlockSelectionConfigComponent blocksConfig, ItemSelectionConfigComponent itemsConfig,
            IMyTerminalBlock referenceBlock)
        {
            return Items.GetProductionBlockItems(blocksConfig, itemsConfig, referenceBlock);
        }

        /// <summary>
        ///     Builds the list of refinery/assembler blocks honouring SelectedBlocks / SelectedGroups / GridLinkType.
        /// </summary>
        private void BuildProductionBlockList(BlockSelectionConfigComponent blocksConfig, IMyTerminalBlock referenceBlock,
            List<IMyTerminalBlock> blocks)
        {
            blocks.Clear();

            if (blocksConfig.SelectedBlocks.Length == 0 && blocksConfig.SelectedGroups.Length == 0)
            {
                var all = GetInventories((GridLinkTypeEnum)blocksConfig.GridLinkTypeInternal);
                for (var i = 0; i < all.Count; i++)
                    if (all[i] is IMyRefinery || all[i] is IMyAssembler)
                        blocks.Add(all[i]);

                return;
            }

            blocks.AddRange(blocksConfig.SelectedBlocks
                .Select(id => MyAPIGateway.Entities.GetEntityById(id) as IMyTerminalBlock)
                .Where(b => (b is IMyRefinery || b is IMyAssembler) &&
                            IsBlockInGridLinkScope(b, referenceBlock, (GridLinkTypeEnum)blocksConfig.GridLinkTypeInternal)));

            if (blocksConfig.SelectedGroups.Length > 0 && referenceBlock != null)
            {
                var groupBlocks = new List<IMyTerminalBlock>();
                foreach (var groupName in blocksConfig.SelectedGroups)
                {
                    groupBlocks.Clear();
                    GridTerminalSystem.GetBlockGroupWithName(groupName)?
                        .GetBlocks(groupBlocks, b => (b is IMyRefinery || b is IMyAssembler) &&
                                                     b.GetUserRelationToOwner(referenceBlock.OwnerId)
                                                     <= MyRelationsBetweenPlayerAndBlock.FactionShare &&
                                                     IsBlockInGridLinkScope(b, referenceBlock, (GridLinkTypeEnum)blocksConfig.GridLinkTypeInternal) &&
                                                     !blocks.Contains(b));
                    blocks.AddRange(groupBlocks);
                }
            }
        }

        private static void ReadInventoryAmounts(IMyTerminalBlock block, int inventoryIndex, List<IngameItem> scratch,
            Dictionary<MyItemType, double> destination)
        {
            if (inventoryIndex >= block.InventoryCount)
                return;

            var inv = block.GetInventory(inventoryIndex);
            if (inv == null)
                return;

            scratch.Clear();
            inv.GetItems(scratch);
            for (var k = 0; k < scratch.Count; k++)
            {
                var it = scratch[k];
                var amount = (double)it.Amount;
                if (amount <= 0)
                    continue;

                var type = it.Type;
                double acc;
                if (destination.TryGetValue(type, out acc))
                    destination[type] = acc + amount;
                else
                    destination[type] = amount;
            }
        }

        private static void FillSortedByAmount(Dictionary<MyItemType, double> source,
            List<KeyValuePair<MyItemType, double>> destination)
        {
            foreach (var kv in source)
                destination.Add(kv);

            destination.Sort((a, b) => b.Value.CompareTo(a.Value));
        }

        private static string GetBlockDisplayName(IMyTerminalBlock block)
        {
            if (!string.IsNullOrEmpty(block.CustomName))
                return block.CustomName;
            if (!string.IsNullOrEmpty(block.DisplayNameText))
                return block.DisplayNameText;
            return block.BlockDefinition.SubtypeName ?? string.Empty;
        }

        private static void SwapBuffer<T>(ref T active, ref T next) where T : class, IList
        {
            var old = active;
            active = next;
            next = old;
            next?.Clear();
        }

        public sealed class ItemsComponent
        {
            readonly GridLogic _owner;

            internal ItemsComponent(GridLogic owner)
            {
                _owner = owner;
            }

            public Dictionary<MyItemType, double> Components
            {
                get
                {
                    if (!_owner._compCache.Any())
                        _owner.AggregateItems(_owner.GetInventories(), _owner._compCache, new[] { "Component" },
                            Array.Empty<MyDefinitionId>());

                    return _owner._compCache;
                }
            }

            public Dictionary<MyItemType, double> Ingots
            {
                get
                {
                    if (!_owner._ingotCache.Any())
                        _owner.AggregateItems(_owner.GetInventories(), _owner._ingotCache, IngotTypeFilter,
                            Array.Empty<MyDefinitionId>());

                    return _owner._ingotCache;
                }
            }

            public Dictionary<MyItemType, double> GetItems(
                BlockSelectionConfigComponent blocksConfig,
                ItemSelectionConfigComponent itemsConfig,
                IMyTerminalBlock referenceBlock,
                string[] types = null)
            {
                return _owner.GetItemsCore(blocksConfig, itemsConfig, referenceBlock, types, _owner._queryCache, false);
            }

            public ItemSnapshot GetItemsSnapshot(
                BlockSelectionConfigComponent blocksConfig,
                ItemSelectionConfigComponent itemsConfig,
                IMyTerminalBlock referenceBlock,
                string[] types = null)
            {
                var searchToken = SearchQueryToken.GetToken(blocksConfig, itemsConfig);
                var items = _owner.GetItemsCore(blocksConfig, itemsConfig, referenceBlock, types, _owner._queryCache, false);

                ItemSnapshot previous;
                if (_owner._itemSnapshots.TryGetValue(searchToken, out previous) &&
                    ItemSnapshot.ContentEquals(previous.Items, items))
                {
                    if (!ReferenceEquals(previous.Items, items))
                        _owner._queryCache[searchToken] = previous.Items;

                    return previous;
                }

                var revision = DateTime.UtcNow;
                if (previous != null && revision <= previous.Revision)
                    revision = previous.Revision.AddTicks(1);

                var snapshot = new ItemSnapshot(searchToken, revision, items);
                _owner._itemSnapshots[searchToken] = snapshot;
                return snapshot;
            }

            public Dictionary<MyItemType, double> GetIngots(
                BlockSelectionConfigComponent blocksConfig,
                ItemSelectionConfigComponent itemsConfig,
                IMyTerminalBlock referenceBlock)
            {
                return _owner.GetItemsCore(blocksConfig, itemsConfig, referenceBlock, IngotTypeFilter,
                    _owner._ingotQueryCache, true);
            }

            public List<ProductionBlockItems> GetProductionBlockItems(
                BlockSelectionConfigComponent blocksConfig,
                ItemSelectionConfigComponent itemsConfig,
                IMyTerminalBlock referenceBlock)
            {
                try
                {
                    var token = SearchQueryToken.GetToken(blocksConfig, itemsConfig);

                    List<ProductionBlockItems> cached;
                    if (_owner._productionByBlockCache.TryGetValue(token, out cached))
                        return cached;

                    var blocks = new List<IMyTerminalBlock>();
                    _owner.BuildProductionBlockList(blocksConfig, referenceBlock, blocks);

                    var result = new List<ProductionBlockItems>(blocks.Count);
                    var scratchItems = new List<IngameItem>();
                    var scratchInput = new Dictionary<MyItemType, double>();
                    var scratchOutput = new Dictionary<MyItemType, double>();

                    for (var b = 0; b < blocks.Count; b++)
                    {
                        var tb = blocks[b];
                        scratchInput.Clear();
                        scratchOutput.Clear();
                        ReadInventoryAmounts(tb, 0, scratchItems, scratchInput);
                        ReadInventoryAmounts(tb, 1, scratchItems, scratchOutput);

                        var entry = new ProductionBlockItems(tb.EntityId, GetBlockDisplayName(tb));
                        FillSortedByAmount(scratchInput, entry.Input);
                        FillSortedByAmount(scratchOutput, entry.Output);
                        result.Add(entry);
                    }

                    _owner._productionByBlockCache[token] = result;
                    return result;
                }
                catch (Exception e)
                {
                    ErrorHandlerHelper.LogError(e, _owner);
                    return new List<ProductionBlockItems>();
                }
            }
        }

        public sealed class BlocksComponent
        {
            readonly GridLogic _owner;

            internal BlocksComponent(GridLogic owner)
            {
                _owner = owner;
            }

            public int LastRefreshIterations => _owner.LastRefreshIterations;
            public int LastRefreshProcessed => _owner.LastRefreshProcessed;
            public int EstimatedNextRefreshBatchSize => _owner.EstimatedNextRefreshBatchSize;
            public int CurrentRefreshBatchSize => _owner.CurrentRefreshBatchSize;
            public bool IsRefreshRunning => _owner.IsRefreshRunning;

            public void RefreshIfNeeded()
            {
                _owner.StartRefresh();
            }

            public List<IMyTerminalBlock> GetInventories()
            {
                RefreshIfNeeded();
                return _owner._invBlocks;
            }

            public List<IMyTerminalBlock> GetInventories(GridLinkTypeEnum linkType)
            {
                var terminals = GetTerminalBlocks<IMyTerminalBlock>(linkType);
                var inventories = new List<IMyTerminalBlock>();
                if (terminals == null)
                    return inventories;

                for (var i = 0; i < terminals.Count; i++)
                {
                    var block = terminals[i];
                    if (block != null && block.HasInventory)
                        inventories.Add(block);
                }

                return inventories;
            }

            internal List<T> GetTerminalBlocksInternal<T>() where T : IMyTerminalBlock
            {
                RefreshIfNeeded();
                switch (typeof(T).Name)
                {
                    case nameof(IMyTerminalBlock):
                        return _owner._terminalBlocks as List<T>;
                    case nameof(IMyCargoContainer):
                        return _owner._cargoContainers as List<T>;
                    case nameof(IMyGasTank):
                        return _owner._gasTanks as List<T>;
                    case nameof(IMyPowerProducer):
                        return _owner._powerProducers as List<T>;
                    case nameof(IMyLaserAntenna):
                        return _owner._lasers as List<T>;
                    case nameof(IMyRadioAntenna):
                        return _owner._radio as List<T>;
                    case nameof(IMyBeacon):
                        return _owner._beacons as List<T>;
                    case nameof(IMyBatteryBlock):
                        return _owner._batteries as List<T>;
                    case nameof(IMyJumpDrive):
                        return _owner._jumpDrives as List<T>;
                    case nameof(IMyAssembler):
                        return _owner._assemblers as List<T>;
                }

                throw new NotImplementedException(typeof(T).Name);
            }

            public List<T> GetTerminalBlocks<T>(GridLinkTypeEnum linkType = (GridLinkTypeEnum)(-1))
                where T : IMyTerminalBlock
            {
                if (linkType == (GridLinkTypeEnum)(-1))
                    return GetTerminalBlocksInternal<T>();

                if (linkType != GridLinkTypeEnum.Physical && linkType != GridLinkTypeEnum.Mechanical)
                    throw new NotImplementedException(typeof(T).Name);

                var resolver = GridGroupLogic.ResolveFor(_owner);
                if (resolver == null)
                    return GetTerminalBlocksInternal<T>();

                return resolver.GetTerminalBlocks<T>(_owner, linkType);
            }

            public List<FarmPlotEntry> GetFarmPlots()
            {
                RefreshIfNeeded();
                return _owner._farmPlots;
            }
        }

        public sealed class PowerComponent
        {
            readonly GridLogic _owner;

            internal PowerComponent(GridLogic owner)
            {
                _owner = owner;
            }

            public List<IMyPowerProducer> GetProducers(GridLinkTypeEnum linkType = (GridLinkTypeEnum)(-1))
            {
                return _owner.GetTerminalBlocks<IMyPowerProducer>(linkType);
            }

            public List<IMyBatteryBlock> GetBatteries(GridLinkTypeEnum linkType = (GridLinkTypeEnum)(-1))
            {
                return _owner.GetTerminalBlocks<IMyBatteryBlock>(linkType);
            }

            public List<IMyJumpDrive> GetJumpDrives(GridLinkTypeEnum linkType = (GridLinkTypeEnum)(-1))
            {
                return _owner.GetTerminalBlocks<IMyJumpDrive>(linkType);
            }
        }

        private struct JumpPointGpsEntry
        {
            public IMyGps Gps;
            public long LastPublishedFrame;
        }
    }
}
