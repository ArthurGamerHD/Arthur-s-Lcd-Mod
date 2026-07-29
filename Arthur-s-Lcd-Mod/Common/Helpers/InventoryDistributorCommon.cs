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
        /// target's free volume. Integer items are split in whole units (largest-remainder), so a
        /// stack of 3 across two equal targets becomes 2 + 1 instead of 1.5 + 1.5 (a fractional
        /// amount makes SE create an invalid stack that only re-syncs on reload).</summary>
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

            foreach (var typeKey in typeKeys)
            {
                bool integral = IsIntegralType(typeKey);

                double total = TotalOfType(source, typeKey, items);
                if (total <= 0)
                    continue;

                if (integral)
                {
                    var shares = IntegerShares((long)Math.Floor(total + 0.5), weights);
                    for (int i = 0; i < targets.Count; i++)
                    {
                        if (shares[i] <= 0)
                            continue;
                        if (TransferAmountOfType(source, targets[i], typeKey, shares[i], true))
                            moved++;
                    }
                }
                else
                {
                    for (int i = 0; i < targets.Count; i++)
                    {
                        double desired = total * weights[i] / totalFree;
                        if (desired <= 0)
                            continue;
                        if (TransferAmountOfType(source, targets[i], typeKey, desired, false))
                            moved++;
                    }
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

            // Weight each container by its capacity; index 0 is the collector and keeps its own share.
            var weights = new double[pool.Count];
            double totalMax = 0;
            for (int i = 0; i < pool.Count; i++)
            {
                weights[i] = (double)pool[i].MaxVolume;
                if (weights[i] < 0)
                    weights[i] = 0;
                totalMax += weights[i];
            }
            if (totalMax <= 0)
                return 0;

            int moved = 0;
            var collector = pool[0];
            var items = new List<MyInventoryItem>();

            foreach (var typeKey in typeKeys)
            {
                bool integral = IsIntegralType(typeKey);

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
                double total = TotalOfType(collector, typeKey, items);
                if (total <= 0)
                    continue;

                // 3. Hand each other container its capacity-proportional share; the collector keeps the
                // rest. Integer items use a whole-unit split (3 across two equal containers -> 2 + 1),
                // never a fractional amount (which spawns an invalid stack that desyncs until reload).
                if (integral)
                {
                    var shares = IntegerShares((long)Math.Floor(total + 0.5), weights);
                    for (int i = 1; i < pool.Count; i++)
                    {
                        if (shares[i] <= 0)
                            continue;
                        if (TransferAmountOfType(collector, pool[i], typeKey, shares[i], true))
                            moved++;
                    }
                }
                else
                {
                    for (int i = 1; i < pool.Count; i++)
                    {
                        double desired = total * weights[i] / totalMax;
                        if (desired <= 0)
                            continue;
                        if (TransferAmountOfType(collector, pool[i], typeKey, desired, false))
                            moved++;
                    }
                }
            }

            return moved;
        }

        /// <summary>Move up to <paramref name="amount"/> of one type from <paramref name="source"/>
        /// into <paramref name="destination"/>. When <paramref name="integral"/> is set the moved
        /// amount is snapped to whole units, so no fractional sliver is ever created.</summary>
        static bool TransferAmountOfType(IMyInventory source, IMyInventory destination, string typeKey,
            double amount, bool integral)
        {
            bool any = false;
            double remaining = integral ? Math.Floor(amount + 0.5) : amount;
            var items = new List<MyInventoryItem>();
            items.Clear();
            source.GetItems(items);

            for (int k = items.Count - 1; k >= 0 && remaining > 0; k--)
            {
                if (KeyOf(items[k].Type) != typeKey)
                    continue;

                double avail = (double)items[k].Amount;
                double take = Math.Min(avail, remaining);
                if (integral)
                    take = Math.Floor(take); // never split a single unit
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

        /// <summary>Sum of one type currently in <paramref name="inventory"/>. <paramref name="scratch"/>
        /// is a reusable buffer (no per-call allocation).</summary>
        static double TotalOfType(IMyInventory inventory, string typeKey, List<MyInventoryItem> scratch)
        {
            double total = 0;
            scratch.Clear();
            inventory.GetItems(scratch);
            for (int k = 0; k < scratch.Count; k++)
            {
                if (KeyOf(scratch[k].Type) == typeKey)
                    total += (double)scratch[k].Amount;
            }

            return total;
        }

        /// <summary>True for item types that only exist in whole units (everything except ore and
        /// ingot, which SE stores by mass and may legitimately be fractional).</summary>
        static bool IsIntegralType(string typeKey)
        {
            if (string.IsNullOrEmpty(typeKey))
                return true;

            int slash = typeKey.IndexOf('/');
            string typeId = slash >= 0 ? typeKey.Substring(0, slash) : typeKey;
            return typeId != "MyObjectBuilder_Ore" && typeId != "MyObjectBuilder_Ingot";
        }

        /// <summary>Splits an integer <paramref name="total"/> across <paramref name="weights"/> with the
        /// largest-remainder (Hamilton) method: every container gets a whole number, the shares sum back
        /// to exactly <paramref name="total"/>, and leftover units go to the largest fractional remainders
        /// (ties broken toward the bigger container). Index 0 is the collector.</summary>
        static long[] IntegerShares(long total, double[] weights)
        {
            int n = weights.Length;
            var result = new long[n];
            if (total <= 0)
                return result;

            double totalWeight = 0;
            for (int i = 0; i < n; i++)
            {
                if (weights[i] < 0)
                    weights[i] = 0;
                totalWeight += weights[i];
            }
            if (totalWeight <= 0)
                return result;

            var remainders = new double[n];
            long assigned = 0;
            for (int i = 0; i < n; i++)
            {
                double ideal = total * weights[i] / totalWeight;
                long baseShare = (long)Math.Floor(ideal);
                result[i] = baseShare;
                remainders[i] = ideal - baseShare;
                assigned += baseShare;
            }

            // Hand the leftover whole units (always fewer than n) to the largest remainders.
            for (long leftover = total - assigned; leftover > 0; leftover--)
            {
                int best = -1;
                for (int i = 0; i < n; i++)
                {
                    if (best < 0
                        || remainders[i] > remainders[best]
                        || (Math.Abs(remainders[i] - remainders[best]) < 0.01 && weights[i] > weights[best]))
                        best = i;
                }

                if (best < 0)
                    break;

                result[best]++;
                remainders[best] = -1; // don't pick the same container twice
            }

            return result;
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
