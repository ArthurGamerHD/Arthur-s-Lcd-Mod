using System;
using System.Collections.Generic;
using LcdMod.Client.GridData;
using VRage.Game.ModAPI;

namespace LcdMod.Client.Modules.Defense
{
    public sealed class DefenseScopeResolver
    {
        public DefenseScopeKey ResolveKey(GridLogic requester, GridLinkTypeEnum linkType)
        {
            var grids = ResolveGrids(requester, linkType);
            var ids = new long[grids.Count];
            for (int i = 0; i < grids.Count; i++)
                ids[i] = grids[i].EntityId;
            Array.Sort(ids);
            return new DefenseScopeKey(linkType, ids);
        }

        public List<IMyCubeGrid> ResolveGrids(GridLogic requester, GridLinkTypeEnum linkType)
        {
            if (requester == null)
                return new List<IMyCubeGrid>();

            var grids = new List<IMyCubeGrid>();
            requester.GetLinkedGrids(linkType, grids);
            if (grids.Count == 0 && requester.Grid != null)
                grids.Add(requester.Grid);
            return grids;
        }
    }
}
