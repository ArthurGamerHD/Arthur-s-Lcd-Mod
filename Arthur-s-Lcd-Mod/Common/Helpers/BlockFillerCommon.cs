using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using VRage;
using VRage.Game;
using IMyInventory = VRage.Game.ModAPI.IMyInventory;
using MyInventoryItem = VRage.Game.ModAPI.Ingame.MyInventoryItem;
using MyItemType = VRage.Game.ModAPI.Ingame.MyItemType;

namespace LcdMod.Common.Helpers
{
    /// <summary>
    /// Tops up weapons (ammo magazines) and reactors (uranium) from a set of source containers.
    /// Must run server-side (or in single-player) so the transfers are authoritative and replicate to
    /// every client. Returns the number of item transfers performed (for user feedback).
    /// </summary>
    public static class BlockFillerCommon
    {
        // Each weapon is topped up to this many ammo magazines (across whatever ammo types it accepts).
        public const int AMMO_PER_WEAPON = 10;

        const string AMMO_TYPE_ID = "MyObjectBuilder_AmmoMagazine";
        const string URANIUM_KEY = "MyObjectBuilder_Ingot/Uranium";

        public static int Execute(List<IMyTerminalBlock> sources, List<IMyTerminalBlock> targets, FillKind kind)
        {
            switch (kind)
            {
                case FillKind.Reactors:
                    return FillReactors(sources, targets);
                default:
                    return FillWeapons(sources, targets, AMMO_PER_WEAPON);
            }
        }

        /// <summary>Top each weapon up to <paramref name="perWeapon"/> ammo magazines, pulling whatever
        /// ammo type the weapon accepts out of the source containers.</summary>
        static int FillWeapons(List<IMyTerminalBlock> sources, List<IMyTerminalBlock> weapons, int perWeapon)
        {
            var sourceInvs = CollectInventories(sources);
            if (sourceInvs.Count == 0 || weapons == null)
                return 0;

            int moved = 0;
            var items = new List<MyInventoryItem>();

            for (int w = 0; w < weapons.Count; w++)
            {
                var weapon = weapons[w];
                if (!(weapon is IMyUserControllableGun) || !weapon.HasInventory)
                    continue;

                var weaponInv = weapon.GetInventory(0);
                if (weaponInv == null)
                    continue;

                double deficit = perWeapon - CountType(weaponInv, AMMO_TYPE_ID, null, items);
                if (deficit <= 0)
                    continue;

                for (int s = 0; s < sourceInvs.Count && deficit > 0; s++)
                {
                    var source = sourceInvs[s];
                    if (source == weaponInv)
                        continue;

                    items.Clear();
                    source.GetItems(items);
                    for (int k = items.Count - 1; k >= 0 && deficit > 0; k--)
                    {
                        var type = items[k].Type;
                        if (type.TypeId != AMMO_TYPE_ID || !weaponInv.CanItemsBeAdded(1, type))
                            continue;

                        double take = Math.Floor(Math.Min((double)items[k].Amount, deficit)); // ammo is integer
                        if (take <= 0)
                            continue;

                        if (Transfer(source, weaponInv, k, (MyFixedPoint)take))
                        {
                            moved++;
                            deficit -= take;
                        }
                    }
                }
            }

            return moved;
        }

        /// <summary>Top each reactor up to its uranium target (which depends on grid size and reactor
        /// size), pulling uranium ingots from the source containers.</summary>
        static int FillReactors(List<IMyTerminalBlock> sources, List<IMyTerminalBlock> reactors)
        {
            var sourceInvs = CollectInventories(sources);
            if (sourceInvs.Count == 0 || reactors == null)
                return 0;

            int moved = 0;
            var items = new List<MyInventoryItem>();

            for (int r = 0; r < reactors.Count; r++)
            {
                var reactor = reactors[r];
                if (!(reactor is IMyReactor) || !reactor.HasInventory)
                    continue;

                var reactorInv = reactor.GetInventory(0);
                if (reactorInv == null)
                    continue;

                double target = GetUraniumTarget(reactor);
                if (target <= 0)
                    continue;

                double deficit = target - CountType(reactorInv, null, URANIUM_KEY, items);
                if (deficit <= 0)
                    continue;

                for (int s = 0; s < sourceInvs.Count && deficit > 0; s++)
                {
                    var source = sourceInvs[s];
                    if (source == reactorInv)
                        continue;

                    items.Clear();
                    source.GetItems(items);
                    for (int k = items.Count - 1; k >= 0 && deficit > 0; k--)
                    {
                        // Uranium is an ingot (mass-based) so a fractional remainder is valid here.
                        if (InventoryDistributorCommon.KeyOf(items[k].Type) != URANIUM_KEY
                            || !reactorInv.CanItemsBeAdded(1, items[k].Type))
                            continue;

                        double take = Math.Min((double)items[k].Amount, deficit);
                        if (take <= 0)
                            continue;

                        if (Transfer(source, reactorInv, k, (MyFixedPoint)take))
                        {
                            moved++;
                            deficit -= take;
                        }
                    }
                }
            }

            return moved;
        }

        // Uranium load target per the user spec: small grid -> 5 (large reactor) / 1 (small reactor);
        // large grid -> 10 (large reactor) / 4 (small reactor).
        static double GetUraniumTarget(IMyTerminalBlock reactor)
        {
            bool gridLarge = reactor.CubeGrid.GridSizeEnum == MyCubeSize.Large;
            bool reactorSmall = IsSmallReactor(reactor);
            if (gridLarge)
                return reactorSmall ? 4d : 10d;
            return reactorSmall ? 1d : 5d;
        }

        // Vanilla reactor subtypes end in "SmallGenerator" / "LargeGenerator"; modded reactors that match
        // neither fall back to the large target.
        static bool IsSmallReactor(IMyTerminalBlock reactor)
        {
            var subtype = reactor.BlockDefinition.SubtypeName ?? string.Empty;
            if (subtype.IndexOf("SmallGenerator", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            return false;
        }

        // Sums the amount of one item, matched either by TypeId (pass typeId) or by full key (pass key).
        static double CountType(IMyInventory inventory, string typeId, string key, List<MyInventoryItem> scratch)
        {
            double total = 0;
            scratch.Clear();
            inventory.GetItems(scratch);
            for (int i = 0; i < scratch.Count; i++)
            {
                if (typeId != null)
                {
                    if (scratch[i].Type.TypeId == typeId)
                        total += (double)scratch[i].Amount;
                }
                else if (InventoryDistributorCommon.KeyOf(scratch[i].Type) == key)
                {
                    total += (double)scratch[i].Amount;
                }
            }

            return total;
        }

        static List<IMyInventory> CollectInventories(List<IMyTerminalBlock> blocks)
        {
            var result = new List<IMyInventory>();
            if (blocks == null)
                return result;

            for (int i = 0; i < blocks.Count; i++)
            {
                var block = blocks[i];
                if (block == null || !block.HasInventory)
                    continue;

                var inv = block.GetInventory(0);
                if (inv != null)
                    result.Add(inv);
            }

            return result;
        }

        static bool Transfer(IMyInventory source, IMyInventory destination, int sourceIndex, MyFixedPoint amount)
        {
            try
            {
                return source.TransferItemTo(destination, sourceIndex, null, true, amount);
            }
            catch (Exception e)
            {
                ErrorHandlerHelper.LogError(e, typeof(BlockFillerCommon));
                return false;
            }
        }
    }
}
