using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;

namespace Graph.Helpers
{
    public static class GridGroupsHelper
    {
        public static void GetAllLogicBlocksOfType<T>(
            IMyCubeGrid rootGrid,
            List<T> results,
            GridLinkTypeEnum linkType)
            where T : class, IMyTerminalBlock
        {
            GetAllLogicBlocksOfType(rootGrid, results, linkType, null);
        }


        public static void GetAllLogicBlocksOfType<T>(
            IMyCubeGrid rootGrid,
            List<T> results,
            GridLinkTypeEnum linkType,
            Func<IMySlimBlock, bool> slimFilter)
            where T : class, IMyTerminalBlock
        {
            if (results == null) return;
            results.Clear();
            if (rootGrid == null) return;

            var grids = new List<IMyCubeGrid>(8);
            try
            {
                MyAPIGateway.GridGroups.GetGroup(rootGrid, linkType, grids);
            }
            catch
            {
            }

            if (grids.Count == 0 || !grids.Contains(rootGrid))
                grids.Add(rootGrid);

            var slims = new List<IMySlimBlock>(128);

            for (var gi = 0; gi < grids.Count; gi++)
            {
                var g = grids[gi];
                slims.Clear();

                try
                {
                    if (slimFilter == null)
                        g.GetBlocks(slims);
                    else
                        g.GetBlocks(slims, slimFilter);
                }
                catch
                {
                    continue;
                }

                for (var i = 0; i < slims.Count; i++)
                {
                    var fat = slims[i].FatBlock;
                    if (fat == null) continue;

                    var casted = fat as T;
                    if (casted != null) results.Add(casted);
                }
            }
        }
    }
}