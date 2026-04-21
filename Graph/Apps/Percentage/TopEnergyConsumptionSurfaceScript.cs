using System;
using System.Collections.Generic;
using Graph.Apps.Abstract;
using Graph.Helpers;
using Sandbox.Game.EntityComponents;
using Sandbox.Game.GameSystems.TextSurfaceScripts;
using Sandbox.ModAPI;
using SpaceEngineers.Game.ModAPI;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.Game.ObjectBuilders.Definitions;
using VRageMath;
using IMyCubeBlock = VRage.Game.ModAPI.IMyCubeBlock;
using IMyCubeGrid = VRage.Game.ModAPI.IMyCubeGrid;
using IMySlimBlock = VRage.Game.ModAPI.IMySlimBlock;

namespace Graph.Apps.Percentage
{
    [MyTextSurfaceScript(ID, TITLE)]
    public class TopEnergyConsumptionSurfaceScript : PercentageSurfaceScript<TopEnergyConsumptionSurfaceScript.Entry>
    {
        public const string ID    = "LCDMod_TopEnergy";
        public const string TITLE = "LCDMod_TopEnergy";

        // Electricity resource type ID used by the SE resource distribution system
        static readonly MyDefinitionId ElectricityId =
            new MyDefinitionId(typeof(MyObjectBuilder_GasProperties), "Electricity");

        // Stored during ReadEntries so GetListPercentageText can reconstruct watts from ratio
        double _maxWatts;

        public TopEnergyConsumptionSurfaceScript(IMyTextSurface surface, IMyCubeBlock block, Vector2 size)
            : base(surface, block, size) { }

        protected override string DefaultTitle => TITLE;

        protected override void ReadEntries(List<Entry> entries)
        {
            CollectConsumers((IMyCubeGrid)Block?.CubeGrid, entries);

            _maxWatts = 0;
            for (int i = 0; i < entries.Count; i++)
                if (entries[i].CurrentWatts > _maxWatts)
                    _maxWatts = entries[i].CurrentWatts;

            if (_maxWatts > 0)
            {
                for (int i = 0; i < entries.Count; i++)
                {
                    var e = entries[i];
                    e.Ratio = (float)(e.CurrentWatts / _maxWatts);
                    entries[i] = e;
                }
            }
        }

        protected override void SortEntries(List<Entry> entries)
        {
            entries.Sort((a, b) =>
            {
                var cmp = b.CurrentWatts.CompareTo(a.CurrentWatts);
                if (cmp != 0) return cmp;
                return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            });

            if (entries.Count > 10)
                entries.RemoveRange(10, entries.Count - 10);
        }

        protected override string GetEntryName(Entry entry) => entry.Name;

        protected override float GetEntryPercentage(Entry entry) => entry.Ratio;

        // Right-side label shows actual wattage rather than a relative percentage
        protected override string GetListPercentageText(float pct)
        {
            return FormatingHelper.WattsToString(pct * _maxWatts);
        }

        protected override string GetGridPercentageText(float pct)
        {
            return FormatingHelper.WattsToString(pct * _maxWatts);
        }

        // Top consumers flagged with warning/error colors to draw attention
        protected override Color? GetEntryUsageColor(float pct)
        {
            if (pct >= 0.90f) return Config.ErrorColor;
            if (pct >= 0.50f) return Config.WarningColor;
            return null;
        }

        void CollectConsumers(IMyCubeGrid rootGrid, List<Entry> entries)
        {
            if (rootGrid == null) return;

            var grids = new List<IMyCubeGrid>();
            try
            {
                MyAPIGateway.GridGroups.GetGroup(rootGrid, GridLinkTypeEnum.Logical, grids);
            }
            catch { }

            var hasRoot = false;
            for (var i = 0; i < grids.Count; i++)
                if (grids[i] == rootGrid) { hasRoot = true; break; }
            if (!hasRoot) grids.Insert(0, rootGrid);

            var slims = new List<IMySlimBlock>();

            for (var gi = 0; gi < grids.Count; gi++)
            {
                var g = grids[gi];
                if (g == null) continue;

                slims.Clear();
                g.GetBlocks(slims);

                for (var i = 0; i < slims.Count; i++)
                {
                    var fat = slims[i].FatBlock as IMyTerminalBlock;
                    if (fat == null) continue;

                    // Batteries excluded: they are power producers/storage, not consumers in this ranking
                    if (fat is IMyBatteryBlock) continue;

                    MyResourceSinkComponent sink = null;
                    try { fat.Components.TryGet(out sink); } catch { }
                    if (sink == null) continue;

                    double watts = 0;
                    try { watts = sink.CurrentInputByType(ElectricityId) * 1000000.0; } catch { }
                    if (watts <= 0) continue;

                    string name;
                    try
                    {
                        name = fat.CustomName;
                        if (string.IsNullOrEmpty(name)) name = fat.DisplayNameText;
                        if (string.IsNullOrEmpty(name)) name = fat.BlockDefinition.SubtypeName;
                        if (string.IsNullOrEmpty(name)) name = "Block";
                    }
                    catch { name = "Block"; }

                    entries.Add(new Entry { Name = name, CurrentWatts = watts });
                }
            }
        }

        public class Entry
        {
            public string Name;
            public double CurrentWatts;
            public float  Ratio;
        }
    }
}
