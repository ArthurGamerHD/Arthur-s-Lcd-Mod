using System;
using System.Collections.Generic;
using Generated;
using Graph.Apps.Abstract;
using Graph.System.TerminalControls.Color;
using Graph.System.TerminalControls.Filter;
using Graph.System.TerminalControls.Filter.Buttons;
using Graph.System.TerminalControls.Filter.Listbox;
using Graph.System.TerminalControls.Generic;
using Graph.System.TerminalControls.Groups;
using Sandbox.Game.GameSystems.TextSurfaceScripts;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRageMath;
using IMyCubeBlock = VRage.Game.ModAPI.IMyCubeBlock;
using IMyCubeGrid = VRage.Game.ModAPI.IMyCubeGrid;
using IMySlimBlock = VRage.Game.ModAPI.IMySlimBlock;

namespace Graph.Apps.Percentage
{
    [MyTextSurfaceScript(ID, TITLE)]
    public partial class CargoFilledSurfaceScript : PercentageSurfaceScript<CargoFilledSurfaceScript.Entry>,
        IUsesTerminalControl<SwitchToggleLines>,
        IUsesTerminalControl<SeparatorFilter>,
        IUsesTerminalControl<LabelSeparator>,
        IUsesTerminalControlGroup<BlocksFilterTerminalControlGroup>
    {
        public const string ID = "ContainerCharts";
        public const string TITLE = "DisplayName_CargoFilledEntityComponent";

        public CargoFilledSurfaceScript(IMyTextSurface surface, IMyCubeBlock block, Vector2 size) : base(surface, block,
            size)
        {
        }

        protected override string DefaultTitle => TITLE;

        protected override void ReadEntries(List<Entry> entries)
        {
            GetContainers((IMyCubeGrid)Block?.CubeGrid, entries);
        }

        protected override void SortEntries(List<Entry> entries)
        {
            entries.Sort((a, b) =>
            {
                var fa = a.Cap > 0 ? a.Used / a.Cap : 0;
                var fb = b.Cap > 0 ? b.Used / b.Cap : 0;
                var cmp = fb.CompareTo(fa);
                if (cmp != 0) return cmp;
                return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            });
        }

        protected override string GetEntryName(Entry entry)
        {
            return entry.Name;
        }

        protected override float GetEntryPercentage(Entry entry)
        {
            if (entry.Cap <= 0) return 0f;
            return (float)(entry.Used / entry.Cap);
        }

        protected override Color? GetEntryUsageColor(float pct)
        {
            if (pct >= .99f)
                return Config.ErrorColor;
            if (pct > .90f)
                return Config.WarningColor;
            return null;
        }

        void GetContainers(IMyCubeGrid rootGrid, List<Entry> details)
        {
            AggregateAllContainersInLogicalGroup(rootGrid, details);
        }

        void AggregateAllContainersInLogicalGroup(IMyCubeGrid rootGrid, List<Entry> details)
        {
            if (rootGrid == null) return;

            var grids = new List<IMyCubeGrid>();
            try
            {
                MyAPIGateway.GridGroups.GetGroup(rootGrid, GridLinkTypeEnum.Logical, grids);
            }
            catch
            {
            }

            var hasRoot = false;
            for (var i = 0; i < grids.Count; i++)
                if (grids[i] == rootGrid)
                {
                    hasRoot = true;
                    break;
                }

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

                    var typeIdStr = "";
                    try
                    {
                        typeIdStr = fat.BlockDefinition.TypeIdString ?? fat.BlockDefinition.TypeId.ToString();
                    }
                    catch
                    {
                    }

                    if (typeIdStr.IndexOf("CargoContainer", StringComparison.OrdinalIgnoreCase) < 0)
                        continue;

                    if (Config != null && Config.SelectedBlocks.Length > 0 &&
                        Array.IndexOf(Config.SelectedBlocks, fat.EntityId) < 0)
                        continue;

                    if (!fat.HasInventory) continue;

                    double localUsed = 0, localCap = 0;
                    var invCount = 0;
                    try
                    {
                        invCount = fat.InventoryCount;
                    }
                    catch
                    {
                    }

                    for (var k = 0; k < invCount; k++)
                    {
                        var inv = fat.GetInventory(k);
                        if (inv == null) continue;
                        try
                        {
                            localUsed += (double)inv.CurrentVolume;
                            localCap += (double)inv.MaxVolume;
                        }
                        catch
                        {
                        }
                    }

                    if (localCap > 0)
                    {
                        string name;
                        try
                        {
                            name = fat.CustomName;
                            if (string.IsNullOrEmpty(name)) name = fat.DisplayNameText;
                            if (string.IsNullOrEmpty(name)) name = fat.BlockDefinition.SubtypeName;
                            if (string.IsNullOrEmpty(name)) name = "Container";
                        }
                        catch
                        {
                            name = "Container";
                        }

                        details.Add(new Entry { Name = name, Used = localUsed, Cap = localCap });
                    }
                }
            }
        }

        public class Entry
        {
            public double Cap;
            public string Name;
            public double Used;
        }
    }
}
