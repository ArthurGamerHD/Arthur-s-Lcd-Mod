using System;
using System.Collections.Generic;
using System.Linq;
using Graph.Helpers;
using Graph.System.Config;
using Sandbox.ModAPI;
using SpaceEngineers.Game.ModAPI;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.Game.ModAPI.Ingame;
using IMyCubeGrid = VRage.Game.ModAPI.IMyCubeGrid;
using IMySlimBlock = VRage.Game.ModAPI.IMySlimBlock;
using IngameItem = VRage.Game.ModAPI.Ingame.MyInventoryItem;

namespace Graph.System
{
    /// <summary>
    /// Logic attached to <see cref="Grid"/>
    /// </summary>
    public class GridLogic
    {
        const int DELAY = 120; 
        long _clock;

        public readonly IMyCubeGrid Grid;
        List<IMySlimBlock> _blocks = new List<IMySlimBlock>();
        List<IMyTerminalBlock> _invBlocks = new List<IMyTerminalBlock>();
        
        List<IMyLaserAntenna> _lasers = new List<IMyLaserAntenna>();
        List<IMyRadioAntenna> _radio = new List<IMyRadioAntenna>();
        List<IMyBeacon> _beacons = new List<IMyBeacon>();
        List<IMyBatteryBlock> _batteries = new List<IMyBatteryBlock>();
        List<IMyJumpDrive> _jumpDrives = new List<IMyJumpDrive>();

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

        /// <summary>
        /// Logic attached to <see cref="grid"/>
        /// </summary>
        /// <param name="grid"></param>
        public GridLogic(IMyCubeGrid grid)
        {
            Grid = grid;
            _clock = new Random().Next(DELAY);
            // Initial Randomization so not every single grid ticks on the same time
        }

        /// <summary>
        /// Update Grid component after specific <see cref="DELAY"/>, Called every tick
        /// </summary>
        public void Update()
        {
            _clock++;
            if (_clock % DELAY != 0)
                return; // skip update by {DELAY} ticks

            try
            {
                _blocks.Clear();
                _compCache.Clear();
                _invBlocks.Clear();
                _queryCache.Clear();
                _refineryInputQueryCache.Clear();
                _refineryOutputQueryCache.Clear();
                _lasers.Clear();
                _radio.Clear();
                _beacons.Clear();
                _batteries.Clear();
                _jumpDrives.Clear();
            }
            catch (Exception e)
            {
                ErrorHandlerHelper.LogError(e, this);
            }
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


        public Dictionary<MyItemType, double> GetItems(ScreenConfig config, IMyTerminalBlock referenceBlock, string[] types = null)
        {
            try
            {
                SearchQueryToken queryToken = SearchQueryToken.GetToken(config);
                Dictionary<MyItemType, double> dictionary;
                if (!_queryCache.TryGetValue(queryToken, out dictionary))
                {
                    dictionary = new Dictionary<MyItemType, double>();

                    List<IMyTerminalBlock> blocks =
                        config.SelectedBlocks.Length == 0 && config.SelectedGroups.Length == 0
                            ? GetInventories()
                            : new List<IMyTerminalBlock>();

                    blocks.AddRange(config.SelectedBlocks.Select(id => MyAPIGateway.Entities.GetEntityById(id))
                        .Select(entity => entity as IMyTerminalBlock)
                        .Where(block =>
                            block != null && block.HasInventory &&
                            block.CubeGrid.IsInSameLogicalGroupAs(referenceBlock.CubeGrid)));

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

        public void RefreshIfNeeded()
        {
            if(_blocks.Any())
                return;
            
            Grid.GetBlocks(_blocks, a => a.FatBlock is IMyTerminalBlock);
            
            
            _invBlocks = _blocks
                .Where(slim => slim.FatBlock is IMyTerminalBlock)
                .Select(slim => (IMyTerminalBlock)slim.FatBlock)
                .Where(block =>
            {
                var antenna = block as IMyRadioAntenna;
                if(antenna != null)
                    _radio.Add(antenna);

                var beacon = block as IMyBeacon;
                if(beacon != null)
                    _beacons.Add(beacon);

                var lase = block as IMyLaserAntenna;
                if(lase != null)
                    _lasers.Add(lase);

                var battery = block as IMyBatteryBlock;
                if(battery != null)
                    _batteries.Add(battery);

                var jumpDrive = block as IMyJumpDrive;
                if (jumpDrive != null)
                    _jumpDrives.Add(jumpDrive);

                return block.HasInventory && block.InventoryCount != 0 ;
            }).Select(a => a).ToList();

        }
        

        public List<IMyLaserAntenna> GetLaserAntennae()
        {
            RefreshIfNeeded();
            return _lasers;
        }
        
        public List<IMyRadioAntenna> GetAntenna()
        {
            RefreshIfNeeded();
            return _radio;
        }
        
        public List<IMyBeacon> GetBeacons()
        {
            RefreshIfNeeded();
            return _beacons;
        }

        public List<IMyBatteryBlock> GetBatteries()
        {
            RefreshIfNeeded();
            return _batteries;
        }

        public List<IMyJumpDrive> GetJumpDrives()
        {
            RefreshIfNeeded();
            return _jumpDrives;
        }

        /// <summary>
        /// Collects items from a single inventory slot of refineries, respecting block/group selection.
        /// </summary>
        /// <param name="inventoryIndex">0 = input (ores), 1 = output (ingots)</param>
        /// <param name="config">Screen config used to filter selected blocks/groups.</param>
        /// <param name="referenceBlock">LCD block used for ownership/grid-group checks.</param>
        /// <returns>Item type/amount dictionary for the requested slot; empty if no refineries found.</returns>
        public Dictionary<MyItemType, double> GetRefineryItems(int inventoryIndex, ScreenConfig config, IMyTerminalBlock referenceBlock)
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
                    var all = GetInventories();
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
                                    b.CubeGrid.IsInSameLogicalGroupAs(referenceBlock.CubeGrid)));

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
    }
}
