using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using VRage;
using VRage.Game;
using IMyInventory = VRage.Game.ModAPI.IMyInventory;
using MyInventoryItem = VRage.Game.ModAPI.Ingame.MyInventoryItem;

namespace LcdMod.Common.Helpers
{
    /// <summary>
    ///     Tops up weapons (ammo magazines) and reactors (uranium) from a set of source containers.
    ///     Must run server-side (or in single-player) so the transfers are authoritative and replicate to
    ///     every client. Returns the number of item transfers performed (for user feedback).
    /// </summary>
    public static class BlockFillerCommon
    {
        private const string AMMO_TYPE_ID = "MyObjectBuilder_AmmoMagazine";
        private const string URANIUM_KEY = "MyObjectBuilder_Ingot/Uranium";

        public static int Execute(List<IMyTerminalBlock> sources, List<IMyTerminalBlock> targets, FillKind kind,
            FillSettings settings = null)
        {
            if (settings == null)
                settings = FillSettings.Defaults();

            switch (kind)
            {
                case FillKind.Reactors:
                    return FillReactors(sources, targets, settings);
                default:
                    return FillWeapons(sources, targets, settings);
            }
        }

        /// <summary>
        ///     Top each weapon up to the magazine count configured for its weapon TYPE (the override
        ///     for the weapon's block SubtypeId, else the global default), pulling whatever ammo type the
        ///     weapon accepts out of the source containers.
        /// </summary>
        private static int FillWeapons(List<IMyTerminalBlock> sources, List<IMyTerminalBlock> weapons,
            FillSettings settings)
        {
            var sourceInvs = CollectInventories(sources);
            if (sourceInvs.Count == 0 || weapons == null)
                return 0;

            var moved = 0;
            var items = new List<MyInventoryItem>();

            for (var w = 0; w < weapons.Count; w++)
            {
                var weapon = weapons[w];
                if (!(weapon is IMyUserControllableGun) || !weapon.HasInventory)
                    continue;

                var weaponInv = weapon.GetInventory(0);
                if (weaponInv == null)
                    continue;

                var target = settings.GetWeaponTarget(weapon.BlockDefinition.SubtypeName);
                var deficit = target - CountType(weaponInv, AMMO_TYPE_ID, null, items);
                if (deficit <= 0)
                    continue;

                for (var s = 0; s < sourceInvs.Count && deficit > 0; s++)
                {
                    var source = sourceInvs[s];
                    if (source == weaponInv)
                        continue;

                    items.Clear();
                    source.GetItems(items);
                    for (var k = items.Count - 1; k >= 0 && deficit > 0; k--)
                    {
                        var type = items[k].Type;
                        if (type.TypeId != AMMO_TYPE_ID || !weaponInv.CanItemsBeAdded(1, type))
                            continue;

                        var take = Math.Floor(Math.Min((double)items[k].Amount, deficit)); // ammo is integer
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

        /// <summary>
        ///     Top each reactor up to its uranium target (which depends on grid size and reactor
        ///     size), pulling uranium ingots from the source containers.
        /// </summary>
        private static int FillReactors(List<IMyTerminalBlock> sources, List<IMyTerminalBlock> reactors,
            FillSettings settings)
        {
            var sourceInvs = CollectInventories(sources);
            if (sourceInvs.Count == 0 || reactors == null)
                return 0;

            var moved = 0;
            var items = new List<MyInventoryItem>();

            for (var r = 0; r < reactors.Count; r++)
            {
                var reactor = reactors[r];
                if (!(reactor is IMyReactor) || !reactor.HasInventory)
                    continue;

                var reactorInv = reactor.GetInventory(0);
                if (reactorInv == null)
                    continue;

                var target = settings.GetUraniumTarget(
                    reactor.CubeGrid.GridSizeEnum == MyCubeSize.Large,
                    IsSmallReactor(reactor));
                if (target <= 0)
                    continue;

                var deficit = target - CountType(reactorInv, null, URANIUM_KEY, items);
                if (deficit <= 0)
                    continue;

                for (var s = 0; s < sourceInvs.Count && deficit > 0; s++)
                {
                    var source = sourceInvs[s];
                    if (source == reactorInv)
                        continue;

                    items.Clear();
                    source.GetItems(items);
                    for (var k = items.Count - 1; k >= 0 && deficit > 0; k--)
                    {
                        if (InventoryDistributorCommon.KeyOf(items[k].Type) != URANIUM_KEY
                            || !reactorInv.CanItemsBeAdded(1, items[k].Type))
                            continue;

                        var take = Math.Min((double)items[k].Amount, deficit);
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

        private static bool IsSmallReactor(IMyTerminalBlock reactor)
        {
            var subtype = reactor.BlockDefinition.SubtypeName ?? string.Empty;
            if (subtype.IndexOf("SmallGenerator", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            return false;
        }

        private static double CountType(IMyInventory inventory, string typeId, string key,
            List<MyInventoryItem> scratch)
        {
            double total = 0;
            scratch.Clear();
            inventory.GetItems(scratch);
            for (var i = 0; i < scratch.Count; i++)
                if (typeId != null)
                {
                    if (scratch[i].Type.TypeId == typeId)
                        total += (double)scratch[i].Amount;
                }
                else if (InventoryDistributorCommon.KeyOf(scratch[i].Type) == key)
                {
                    total += (double)scratch[i].Amount;
                }

            return total;
        }

        private static List<IMyInventory> CollectInventories(List<IMyTerminalBlock> blocks)
        {
            var result = new List<IMyInventory>();
            if (blocks == null)
                return result;

            for (var i = 0; i < blocks.Count; i++)
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

        private static bool Transfer(IMyInventory source, IMyInventory destination, int sourceIndex,
            MyFixedPoint amount)
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