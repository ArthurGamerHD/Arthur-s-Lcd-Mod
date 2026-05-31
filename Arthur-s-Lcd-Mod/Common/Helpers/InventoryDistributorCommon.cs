using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using VRage;
using IMyInventory = VRage.Game.ModAPI.IMyInventory;
using MyInventoryItem = VRage.Game.ModAPI.Ingame.MyInventoryItem;
using MyItemType = VRage.Game.ModAPI.Ingame.MyItemType;

namespace LcdMod.Common.Helpers
{
    /// <summary>
    /// Item moves driven by the container action dialog (Send / Receive / Balance). Item types are
    /// matched by a "TypeId/SubtypeId" key so the selection survives the network hop without
    /// reconstructing <c>MyItemType</c>. Must run server-side (or in single-player) so the transfers
    /// are authoritative. Returns the number of item transfers performed (for user feedback).
    /// </summary>
    public static class InventoryDistributorCommon
    {
        public static string KeyOf(MyItemType type)
        {
            return type.TypeId + "/" + type.SubtypeId;
        }

        public static int Execute(IMyTerminalBlock source, List<IMyTerminalBlock> targets,
            HashSet<string> typeKeys, TransferMode mode)
        {
            if (source == null || !source.HasInventory || typeKeys == null || typeKeys.Count == 0)
                return 0;

            var sourceInv = source.GetInventory(0);
            if (sourceInv == null)
                return 0;

            var targetInvs = new List<IMyInventory>();
            if (targets != null)
            {
                for (int i = 0; i < targets.Count; i++)
                {
                    var block = targets[i];
                    if (block == null || !block.HasInventory || block == source)
                        continue;

                    var inv = block.GetInventory(0);
                    if (inv != null)
                        targetInvs.Add(inv);
                }
            }

            if (targetInvs.Count == 0)
                return 0;

            switch (mode)
            {
                case TransferMode.Receive:
                    return PullInto(sourceInv, targetInvs, typeKeys);
                case TransferMode.Balance:
                    var pool = new List<IMyInventory>(targetInvs.Count + 1);
                    pool.Add(sourceInv);
                    pool.AddRange(targetInvs);
                    return Balance(pool, typeKeys);
                default: // Send
                    return DistributeFrom(sourceInv, targetInvs, typeKeys);
            }
        }

        /// <summary>Send: spread the source's matching items across the targets, proportional to each
        /// target's free volume.</summary>
        static int DistributeFrom(IMyInventory source, List<IMyInventory> targets, HashSet<string> typeKeys)
        {
            double totalFree = 0;
            var weights = new double[targets.Count];
            for (int i = 0; i < targets.Count; i++)
            {
                weights[i] = GetFreeVolume(targets[i]);
                if (weights[i] < 0)
                    weights[i] = 0;
                totalFree += weights[i];
            }

            if (totalFree <= 0)
                return 0;

            int moved = 0;
            var items = new List<MyInventoryItem>();
            items.Clear();
            source.GetItems(items);

            // Reverse: transferring a (shrinking) stack at index k keeps lower indices valid.
            for (int k = items.Count - 1; k >= 0; k--)
            {
                if (!Matches(items[k].Type, typeKeys))
                    continue;

                double amount = (double)items[k].Amount;
                for (int i = 0; i < targets.Count; i++)
                {
                    double share = amount * weights[i] / totalFree;
                    if (share <= 0)
                        continue;

                    if (Transfer(source, targets[i], k, (MyFixedPoint)share))
                        moved++;
                }
            }

            return moved;
        }

        /// <summary>Receive: pull the targets' matching items into the source (whole stacks, merged).</summary>
        static int PullInto(IMyInventory destination, List<IMyInventory> sources, HashSet<string> typeKeys)
        {
            int moved = 0;
            var items = new List<MyInventoryItem>();

            for (int s = 0; s < sources.Count; s++)
            {
                var source = sources[s];
                items.Clear();
                source.GetItems(items);

                for (int k = items.Count - 1; k >= 0; k--)
                {
                    if (!Matches(items[k].Type, typeKeys))
                        continue;

                    if (Transfer(source, destination, k, null))
                        moved++;
                }
            }

            return moved;
        }

        /// <summary>Balance: for each chosen type, gather everything into pool[0] then hand each
        /// container an amount proportional to its capacity (so all end up at a similar fill).</summary>
        static int Balance(List<IMyInventory> pool, HashSet<string> typeKeys)
        {
            if (pool.Count < 2)
                return 0;

            double totalMax = 0;
            for (int i = 0; i < pool.Count; i++)
                totalMax += (double)pool[i].MaxVolume;
            if (totalMax <= 0)
                return 0;

            int moved = 0;
            var collector = pool[0];
            var items = new List<MyInventoryItem>();

            foreach (var typeKey in typeKeys)
            {
                // 1. Gather all of this type into the collector.
                for (int i = 1; i < pool.Count; i++)
                {
                    items.Clear();
                    pool[i].GetItems(items);
                    for (int k = items.Count - 1; k >= 0; k--)
                    {
                        if (KeyOf(items[k].Type) != typeKey)
                            continue;

                        if (Transfer(pool[i], collector, k, null))
                            moved++;
                    }
                }

                // 2. Total of this type now in the collector.
                double total = 0;
                items.Clear();
                collector.GetItems(items);
                for (int k = 0; k < items.Count; k++)
                {
                    if (KeyOf(items[k].Type) == typeKey)
                        total += (double)items[k].Amount;
                }

                if (total <= 0)
                    continue;

                // 3. Hand each other container its capacity-proportional share; the collector keeps the rest.
                for (int i = 1; i < pool.Count; i++)
                {
                    double desired = total * (double)pool[i].MaxVolume / totalMax;
                    if (desired <= 0)
                        continue;

                    if (TransferAmountOfType(collector, pool[i], typeKey, desired))
                        moved++;
                }
            }

            return moved;
        }

        /// <summary>Move up to <paramref name="amount"/> of one type from <paramref name="source"/>
        /// into <paramref name="destination"/>.</summary>
        static bool TransferAmountOfType(IMyInventory source, IMyInventory destination, string typeKey, double amount)
        {
            bool any = false;
            double remaining = amount;
            var items = new List<MyInventoryItem>();
            items.Clear();
            source.GetItems(items);

            for (int k = items.Count - 1; k >= 0 && remaining > 0; k--)
            {
                if (KeyOf(items[k].Type) != typeKey)
                    continue;

                double avail = (double)items[k].Amount;
                double take = Math.Min(avail, remaining);
                if (take <= 0)
                    continue;

                if (Transfer(source, destination, k, (MyFixedPoint)take))
                {
                    any = true;
                    remaining -= take;
                }
            }

            return any;
        }

        static bool Transfer(IMyInventory source, IMyInventory destination, int sourceIndex, MyFixedPoint? amount)
        {
            try
            {
                return source.TransferItemTo(destination, sourceIndex, null, true, amount);
            }
            catch (Exception e)
            {
                ErrorHandlerHelper.LogError(e, typeof(InventoryDistributorCommon));
                return false;
            }
        }

        static bool Matches(MyItemType type, HashSet<string> typeKeys)
        {
            return typeKeys.Contains(KeyOf(type));
        }

        static double GetFreeVolume(IMyInventory inventory)
        {
            return (double)inventory.MaxVolume - (double)inventory.CurrentVolume;
        }
    }
}
