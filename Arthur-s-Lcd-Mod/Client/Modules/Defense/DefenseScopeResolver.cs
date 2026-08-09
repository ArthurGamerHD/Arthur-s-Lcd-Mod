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

            var resolver = GridGroupLogic.ResolveFor(requester);
            if (resolver != null)
                return resolver.GetGrids(requester, linkType);

            var fallback = new List<IMyCubeGrid>();
            if (requester.Grid != null)
                fallback.Add(requester.Grid);
            return fallback;
        }
    }
}
