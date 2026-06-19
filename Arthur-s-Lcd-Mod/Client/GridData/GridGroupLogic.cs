using System;
using System.Collections.Generic;
using LcdMod.Common.Helpers;
using Sandbox.ModAPI;
using IMyCubeGrid = VRage.Game.ModAPI.IMyCubeGrid;
using IMySlimBlock = VRage.Game.ModAPI.IMySlimBlock;
using GridLinkTypeEnum = VRage.Game.ModAPI.GridLinkTypeEnum;

namespace LcdMod.Client.GridData
{
    /// <summary>
    /// Resolves cross-grid block queries for a mechanical grid group.
    /// Each <see cref="GridLogic"/> keeps its own local block buffers; this resolver is only used
    /// by the GetTerminalBlocks overload that explicitly asks for a grid-link scope.
    /// </summary>
    public sealed class GridGroupLogic
    {
        readonly GridLogic _owner;
        readonly List<IMyCubeGrid> _mechanicalGrids = new List<IMyCubeGrid>();
        readonly List<IMyCubeGrid> _physicalGrids = new List<IMyCubeGrid>();
        readonly HashSet<long> _mechanicalGridIds = new HashSet<long>();
        readonly HashSet<long> _dedupeGridIds = new HashSet<long>();
        readonly HashSet<long> _dedupeBlockIds = new HashSet<long>();

        public readonly List<GridLogic> MechanicalConnections = new List<GridLogic>();
        public readonly List<GridLogic> PhysicalConnections = new List<GridLogic>();

        public GridLogic Owner => _owner;

        public GridGroupLogic(GridLogic owner)
        {
            _owner = owner;
        }

        
        /// <summary>
        /// Gets the current active GridGroupLogic for the largest grid in the cluster
        /// </summary>
        /// <param name="logic"></param>
        /// <returns></returns>
        public static GridGroupLogic ResolveFor(GridLogic logic)
        {
            if (logic == null)
                return null;

            var owner = FindMechanicalGroupOwner(logic);
            if (owner == null)
                owner = logic;

            var resolver = owner.GetLocalGridGroupResolver();
            AssignResolverToMechanicalGroup(owner, resolver);
            return resolver;
        }

        public List<T> GetTerminalBlocks<T>(GridLogic requester, GridLinkTypeEnum linkType) where T : IMyTerminalBlock
        {
            var result = new List<T>();
            if (requester == null)
                return result;

            Refresh(requester);

            _dedupeBlockIds.Clear();
            AddLocalBlocks(result, requester);

            if (ShouldIncludeMechanical(linkType))
            {
                if (_owner != null && _owner != requester)
                    AddLocalBlocks(result, _owner);

                for (int i = 0; i < MechanicalConnections.Count; i++)
                    AddLocalBlocks(result, MechanicalConnections[i]);
            }

            if (ShouldIncludePhysical(linkType))
            {
                for (int i = 0; i < PhysicalConnections.Count; i++)
                    AddLocalBlocks(result, PhysicalConnections[i]);
            }

            _dedupeBlockIds.Clear();
            return result;
        }

        public List<GridLogic> GetGridLogics(GridLogic requester, GridLinkTypeEnum linkType)
        {
            var result = new List<GridLogic>();
            var grids = GetGrids(requester, linkType);
            for (int i = 0; i < grids.Count; i++)
            {
                var logic = LcdModSessionComponent.GetOrCreateGridLogic(grids[i]);
                if (logic != null && !result.Contains(logic))
                    result.Add(logic);
            }

            return result;
        }

        public List<IMyCubeGrid> GetGrids(GridLogic requester, GridLinkTypeEnum linkType)
        {
            var result = new List<IMyCubeGrid>();
            if (requester == null)
                return result;

            Refresh(requester);

            _dedupeGridIds.Clear();
            AddGrid(result, requester.Grid);

            if (ShouldIncludeMechanical(linkType))
            {
                if (_owner != null && _owner != requester)
                    AddGrid(result, _owner.Grid);

                for (int i = 0; i < MechanicalConnections.Count; i++)
                    AddGrid(result, MechanicalConnections[i] != null ? MechanicalConnections[i].Grid : null);
            }

            if (ShouldIncludePhysical(linkType))
            {
                for (int i = 0; i < PhysicalConnections.Count; i++)
                    AddGrid(result, PhysicalConnections[i] != null ? PhysicalConnections[i].Grid : null);
            }

            result.Sort(CompareGridEntityId);
            _dedupeGridIds.Clear();
            return result;
        }

        void Refresh(GridLogic requester)
        {
            MechanicalConnections.Clear();
            PhysicalConnections.Clear();
            _mechanicalGridIds.Clear();

            var mechanicalRoot = _owner ?? requester;
            if (mechanicalRoot == null || mechanicalRoot.Grid == null)
                return;

            _mechanicalGrids.Clear();
            if (MyAPIGateway.GridGroups != null)
                MyAPIGateway.GridGroups.GetGroup(mechanicalRoot.Grid, GridLinkTypeEnum.Mechanical, _mechanicalGrids);

            if (_mechanicalGrids.Count == 0)
                _mechanicalGrids.Add(mechanicalRoot.Grid);

            for (int i = 0; i < _mechanicalGrids.Count; i++)
            {
                var grid = _mechanicalGrids[i];
                if (grid == null)
                    continue;

                _mechanicalGridIds.Add(grid.EntityId);

                if (mechanicalRoot.Grid != null && grid.EntityId == mechanicalRoot.Grid.EntityId)
                    continue;

                var logic = LcdModSessionComponent.GetOrCreateGridLogic(grid);
                if (logic != null && !MechanicalConnections.Contains(logic))
                    MechanicalConnections.Add(logic);
            }

            _physicalGrids.Clear();
            if (MyAPIGateway.GridGroups != null)
                MyAPIGateway.GridGroups.GetGroup(mechanicalRoot.Grid, GridLinkTypeEnum.Physical, _physicalGrids);

            for (int i = 0; i < _physicalGrids.Count; i++)
            {
                var grid = _physicalGrids[i];
                if (grid == null)
                    continue;

                if (_mechanicalGridIds.Contains(grid.EntityId))
                    continue;

                var logic = LcdModSessionComponent.GetOrCreateGridLogic(grid);
                if (logic != null && !PhysicalConnections.Contains(logic))
                    PhysicalConnections.Add(logic);
            }
        }

        static void AssignResolverToMechanicalGroup(GridLogic owner, GridGroupLogic resolver)
        {
            if (owner == null || resolver == null || owner.Grid == null)
                return;

            var grids = new List<IMyCubeGrid>();
            if (MyAPIGateway.GridGroups != null)
                MyAPIGateway.GridGroups.GetGroup(owner.Grid, GridLinkTypeEnum.Mechanical, grids);

            if (grids.Count == 0)
                grids.Add(owner.Grid);

            for (int i = 0; i < grids.Count; i++)
            {
                var grid = grids[i];
                if (grid == null)
                    continue;

                var logic = LcdModSessionComponent.GetOrCreateGridLogic(grid);
                if (logic != null)
                    logic.SetGridGroupResolver(resolver);
            }
        }

        static GridLogic FindMechanicalGroupOwner(GridLogic rootLogic)
        {
            if (rootLogic == null || rootLogic.Grid == null)
                return rootLogic;

            var grids = new List<IMyCubeGrid>();
            if (MyAPIGateway.GridGroups != null)
                MyAPIGateway.GridGroups.GetGroup(rootLogic.Grid, GridLinkTypeEnum.Mechanical, grids);

            if (grids.Count == 0)
                grids.Add(rootLogic.Grid);

            IMyCubeGrid bestGrid = null;
            int bestCount = -1;

            for (int i = 0; i < grids.Count; i++)
            {
                var grid = grids[i];
                if (grid == null)
                    continue;

                int count = CountGridBlocks(grid);
                if (bestGrid == null || count > bestCount || (count == bestCount && grid.EntityId < bestGrid.EntityId))
                {
                    bestGrid = grid;
                    bestCount = count;
                }
            }

            if (bestGrid == null)
                return rootLogic;

            return LcdModSessionComponent.GetOrCreateGridLogic(bestGrid) ?? rootLogic;
        }

        static int CountGridBlocks(IMyCubeGrid grid)
        {
            if (grid == null)
                return 0;

            var resolver = new List<IMySlimBlock>();
            try
            {
                grid.GetBlocks(resolver);
                return resolver.Count;
            }
            catch (Exception e)
            {
                ErrorHandlerHelper.LogError(e, typeof(GridGroupLogic));
                return 0;
            }
        }

        static bool ShouldIncludeMechanical(GridLinkTypeEnum linkType)
        {
            return linkType == GridLinkTypeEnum.Mechanical ||
                   linkType == GridLinkTypeEnum.Logical ||
                   linkType == GridLinkTypeEnum.Physical ||
                   linkType == GridLinkTypeEnum.Electrical;
        }

        static bool ShouldIncludePhysical(GridLinkTypeEnum linkType)
        {
            return linkType == GridLinkTypeEnum.Physical;
        }

        void AddLocalBlocks<T>(List<T> result, GridLogic logic) where T : IMyTerminalBlock
        {
            if (result == null || logic == null)
                return;

            var blocks = logic.GetTerminalBlocksInternal<T>();
            if (blocks == null)
                return;

            for (int i = 0; i < blocks.Count; i++)
            {
                var block = blocks[i];
                if (block == null)
                    continue;

                if (!_dedupeBlockIds.Add(block.EntityId))
                    continue;

                result.Add(block);
            }
        }

        void AddGrid(List<IMyCubeGrid> result, IMyCubeGrid grid)
        {
            if (result == null || grid == null)
                return;

            if (!_dedupeGridIds.Add(grid.EntityId))
                return;

            result.Add(grid);
        }

        static int CompareGridEntityId(IMyCubeGrid left, IMyCubeGrid right)
        {
            long a = left?.EntityId ?? 0;
            long b = right?.EntityId ?? 0;
            return a.CompareTo(b);
        }
    }
}
