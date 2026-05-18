using System;
using System.Collections.Generic;
using LcdMod.Common.Helpers;
using LcdMod.Client.SurfaceScripts;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using IMyCubeGrid = VRage.Game.ModAPI.IMyCubeGrid;
using IMySlimBlock = VRage.Game.ModAPI.IMySlimBlock;

namespace LcdMod.Client.Apps
{
    public sealed class CargoFilledApp
    {
        readonly CargoFilledSurfaceScript _script;

        public CargoFilledApp(CargoFilledSurfaceScript script)
        {
            _script = script;
        }

        public void ReadEntries(List<CargoFilledSurfaceScript.Entry> details)
        {
            AggregateAllContainersInLogicalGroup(_script.Block?.CubeGrid, details);
        }

        void AggregateAllContainersInLogicalGroup(IMyCubeGrid rootGrid, List<CargoFilledSurfaceScript.Entry> details)
        {
            if (rootGrid == null)
                return;

            var grids = new List<IMyCubeGrid>();
            try
            {
                MyAPIGateway.GridGroups.GetGroup(rootGrid, GridLinkTypeEnum.Logical, grids);
            }
            catch (Exception e)
            {
                ErrorHandlerHelper.LogError(e, _script);
            }

            var hasRoot = false;
            for (var i = 0; i < grids.Count; i++)
            {
                if (grids[i] != rootGrid)
                    continue;
                hasRoot = true;
                break;
            }

            if (!hasRoot)
                grids.Insert(0, rootGrid);

            var slims = new List<IMySlimBlock>();
            for (var gi = 0; gi < grids.Count; gi++)
            {
                var g = grids[gi];
                if (g == null)
                    continue;

                slims.Clear();
                g.GetBlocks(slims);

                for (var i = 0; i < slims.Count; i++)
                {
                    var fat = slims[i].FatBlock as IMyTerminalBlock;
                    if (fat == null)
                        continue;

                    var typeIdStr = string.Empty;
                    try
                    {
                        typeIdStr = fat.BlockDefinition.TypeIdString ?? fat.BlockDefinition.TypeId.ToString();
                    }
                    catch (Exception e)
                    {
                        ErrorHandlerHelper.LogError(e, _script);
                    }

                    if (typeIdStr.IndexOf("CargoContainer", StringComparison.OrdinalIgnoreCase) < 0)
                        continue;

                    var config = _script.BlocksConfig;
                    if (config != null && config.SelectedBlocks.Length > 0 &&
                        Array.IndexOf(config.SelectedBlocks, fat.EntityId) < 0)
                        continue;

                    if (!fat.HasInventory)
                        continue;

                    double localUsed = 0;
                    double localCap = 0;
                    var invCount = 0;
                    try
                    {
                        invCount = fat.InventoryCount;
                    }
                    catch (Exception e)
                    {
                        ErrorHandlerHelper.LogError(e, _script);
                    }

                    for (var k = 0; k < invCount; k++)
                    {
                        var inv = fat.GetInventory(k);
                        if (inv == null)
                            continue;
                        try
                        {
                            localUsed += (double)inv.CurrentVolume;
                            localCap += (double)inv.MaxVolume;
                        }
                        catch (Exception e)
                        {
                            ErrorHandlerHelper.LogError(e, _script);
                        }
                    }

                    if (localCap <= 0)
                        continue;

                    string name;
                    try
                    {
                        name = fat.CustomName;
                        if (string.IsNullOrEmpty(name))
                            name = fat.DisplayNameText;
                        if (string.IsNullOrEmpty(name))
                            name = fat.BlockDefinition.SubtypeName;
                        if (string.IsNullOrEmpty(name))
                            name = "Container";
                    }
                    catch
                    {
                        name = "Container";
                    }

                    details.Add(new CargoFilledSurfaceScript.Entry { Name = name, Used = localUsed, Cap = localCap });
                }
            }
        }
    }
}
