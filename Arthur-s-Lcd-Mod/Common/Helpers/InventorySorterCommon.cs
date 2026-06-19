using System;
using System.Collections.Generic;
using Sandbox.Definitions;
using Sandbox.ModAPI;
using VRage.Game;
using IMyInventory = VRage.Game.ModAPI.IMyInventory;
using MyInventoryItem = VRage.Game.ModAPI.Ingame.MyInventoryItem;
using MyItemType = VRage.Game.ModAPI.Ingame.MyItemType;

namespace LcdMod.Common.Helpers
{
    /// <summary>
    /// Consolidates the items of a set of blocks. Cargo containers and connectors are both drained
    /// and filled; assemblers, refineries and ship tools (welder/grinder/drill) are drained from
    /// all of their inventories but are never used as a destination. Items are packed, in the chosen
    /// <see cref="InventorySortMode"/> priority
    /// order, into the destination with the most free volume; once it reaches
    /// <see cref="FILL_THRESHOLD"/> of its capacity the packing moves on to the next emptiest one,
    /// merging duplicate stacks (a destination keeps a single stack per stackable item type). Must
    /// run server-side (or in single-player) so the transfers are authoritative and replicate to
    /// all clients. Returns the number of item transfers performed (for user feedback).
    /// </summary>
    public static class InventorySorterCommon
    {
        // A container counts as "full" (stop pouring into it, move to the next emptiest) once it
        // reaches this fraction of its capacity. Kept high so everything piles into the emptiest
        // container and the others are left empty whenever the contents fit.
        const double FILL_THRESHOLD = 0.98;

        public static int Consolidate(List<IMyTerminalBlock> blocks, InventorySortMode mode)
        {
            if (blocks == null || blocks.Count < 2)
                return 0;

            // Roles by block type:
            //  - Cargo containers and connectors: drained AND filled (source + destination).
            //  - Assemblers and refineries: drained from their OUTPUT inventory only (collect the
            //    finished products); never touched as a destination and never drained from their
            //    input queue, so active production is left alone.
            var destinations = new List<IMyInventory>(blocks.Count);
            var sources = new List<IMyInventory>(blocks.Count);

            for (int i = 0; i < blocks.Count; i++)
            {
                var block = blocks[i];
                if (block == null || !block.HasInventory)
                    continue;

                if (block is IMyCargoContainer || block is IMyShipConnector)
                {
                    var inv = block.GetInventory(0);
                    if (inv != null)
                    {
                        destinations.Add(inv);
                        sources.Add(inv);
                    }
                }
                else if (block is IMyAssembler || block is IMyRefinery || block is IMyShipToolBase)
                {
                    // Production blocks (assembler/refinery) and ship tools (welder/grinder/drill):
                    // drain every inventory; they are never used as a destination.
                    for (int inv = 0; inv < block.InventoryCount; inv++)
                    {
                        var drained = block.GetInventory(inv);
                        if (drained != null)
                            sources.Add(drained);
                    }
                }
            }

            if (destinations.Count == 0 || sources.Count < 2)
                return 0;

            // Destinations, emptiest first. The index only moves forward: we pour EVERYTHING into
            // the emptiest container until it is full, then spill into the next emptiest, so equal
            // items merge (stack:true) into a single stack and the drained containers end up empty.
            var targets = new List<IMyInventory>(destinations);
            targets.Sort((a, b) => GetFreeVolume(b).CompareTo(GetFreeVolume(a)));

            var typeOrder = BuildSortedTypeList(sources, mode);

            int moved = 0;
            int destIndex = 0;
            var items = new List<MyInventoryItem>();

            for (int t = 0; t < typeOrder.Count; t++)
            {
                var type = typeOrder[t];

                for (int s = 0; s < sources.Count; s++)
                {
                    var source = sources[s];

                    // Drain every stack of this type out of `source` into the current destination.
                    // If the destination fills up mid-drain we advance and re-read the source, so a
                    // partially-transferred stack's remainder continues into the next container.
                    bool keepDraining = true;
                    int safety = 0;
                    while (keepDraining && safety++ < 100000)
                    {
                        keepDraining = false;

                        while (destIndex < targets.Count && IsFull(targets[destIndex]))
                            destIndex++;
                        if (destIndex >= targets.Count)
                            return moved; // everything is full

                        var dest = targets[destIndex];
                        if (ReferenceEquals(dest, source))
                            break; // this source already IS the destination; leave its items here

                        items.Clear();
                        source.GetItems(items);

                        // Reverse order keeps the captured source indices valid as items are removed.
                        for (int k = items.Count - 1; k >= 0; k--)
                        {
                            if (!items[k].Type.Equals(type))
                                continue;

                            double beforeDest = (double)dest.CurrentVolume;
                            try
                            {
                                // amount = null moves the WHOLE stack and merges it with a matching
                                // stack in the destination. Passing an explicit amount makes SE split
                                // off a new stack instead (the "needs a 2nd sort to merge" bug).
                                source.TransferItemTo(dest, k, null, true, null);
                            }
                            catch (Exception e)
                            {
                                ErrorHandlerHelper.LogError(e, typeof(InventorySorterCommon));
                            }

                            if ((double)dest.CurrentVolume > beforeDest)
                                moved++;

                            if (IsFull(dest))
                            {
                                // Destination is full; re-pick the next one and re-read the source.
                                keepDraining = true;
                                break;
                            }
                        }
                    }
                }
            }

            return moved;
        }

        /// <summary>
        /// Distinct item types found across <paramref name="inventories"/>, ordered by the chosen
        /// criterion. Items are then packed into containers in this order.
        /// </summary>
        static List<MyItemType> BuildSortedTypeList(List<IMyInventory> inventories, InventorySortMode mode)
        {
            var amounts = new Dictionary<MyItemType, double>();
            var items = new List<MyInventoryItem>();

            for (int i = 0; i < inventories.Count; i++)
            {
                items.Clear();
                inventories[i].GetItems(items);
                for (int k = 0; k < items.Count; k++)
                {
                    var type = items[k].Type;
                    double current;
                    amounts.TryGetValue(type, out current);
                    amounts[type] = current + (double)items[k].Amount;
                }
            }

            var types = new List<MyItemType>(amounts.Keys);
            switch (mode)
            {
                case InventorySortMode.Weight:
                    types.Sort((a, b) => GetTotalWeight(b, amounts[b]).CompareTo(GetTotalWeight(a, amounts[a])));
                    break;
                case InventorySortMode.Alphabetical:
                    types.Sort((a, b) => string.Compare(GetDisplayName(a), GetDisplayName(b), StringComparison.OrdinalIgnoreCase));
                    break;
                default: // Quantity
                    types.Sort((a, b) => amounts[b].CompareTo(amounts[a]));
                    break;
            }

            return types;
        }

        static double GetTotalWeight(MyItemType type, double amount)
        {
            var def = GetDefinition(type);
            return amount * def?.Mass ?? 0d;
        }

        static string GetDisplayName(MyItemType type)
        {
            var def = GetDefinition(type);
            if (def != null && !string.IsNullOrEmpty(def.DisplayNameText))
                return def.DisplayNameText;

            return type.SubtypeId ?? string.Empty;
        }

        static MyPhysicalItemDefinition GetDefinition(MyItemType type)
        {
            try
            {
                MyDefinitionId id;
                if (!MyDefinitionId.TryParse(type.TypeId + "/" + type.SubtypeId, out id))
                    return null;

                return MyDefinitionManager.Static.GetPhysicalItemDefinition(id);
            }
            catch (Exception)
            {
                return null;
            }
        }

        static bool IsFull(IMyInventory inventory)
        {
            double max = (double)inventory.MaxVolume;
            if (max <= 0d)
                return true;

            return (double)inventory.CurrentVolume / max >= FILL_THRESHOLD;
        }

        static double GetFreeVolume(IMyInventory inventory)
        {
            return (double)inventory.MaxVolume - (double)inventory.CurrentVolume;
        }
    }
}
