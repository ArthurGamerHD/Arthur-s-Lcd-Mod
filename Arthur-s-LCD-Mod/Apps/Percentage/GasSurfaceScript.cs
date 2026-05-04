using System;
using System.Collections.Generic;
using Generated;
using Graph.Apps.Abstract;
using Graph.Helpers;
using Graph.System.TerminalControls.Color;
using Graph.System.TerminalControls.Generic;
using Graph.System.TerminalControls.Groups;
using Sandbox.Definitions;
using Sandbox.Game.GameSystems.TextSurfaceScripts;
using Sandbox.ModAPI;
using VRage;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.Game.ObjectBuilders.Definitions;
using VRageMath;
using IMyCubeBlock = VRage.Game.ModAPI.IMyCubeBlock;
using IMyCubeGrid = VRage.Game.ModAPI.IMyCubeGrid;
using IMySlimBlock = VRage.Game.ModAPI.IMySlimBlock;
using IMyTerminalBlock = Sandbox.ModAPI.IMyTerminalBlock;

namespace Graph.Apps.Percentage
{
    [MyTextSurfaceScript(ID, TITLE)]
    public partial class GasSurfaceScript : PercentageSurfaceScript<GasSurfaceScript.Entry>
    {
        public const string ID = "GasGraph";
        public const string TITLE = "LCDMod_GasFilled";

        readonly Dictionary<string, string> _gasDisplayNameCache =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public GasSurfaceScript(IMyTextSurface surface, IMyCubeBlock block, Vector2 size) : base(surface, block, size)
        {
        }

        protected override string DefaultTitle => TITLE;

        protected override int GetMaxColsFromSurface() => 1;

        protected override void ReadEntries(List<Entry> entries)
        {
            string mode;
            string token;
            ParseFilter(Block as IMyTerminalBlock, out mode, out token);

            var rootGrid = (IMyCubeGrid)Block?.CubeGrid;
            if (rootGrid == null) return;

            var grids = new List<IMyCubeGrid>();
            try
            {
                MyAPIGateway.GridGroups.GetGroup(rootGrid, GridLinkTypeEnum.Logical, grids);
            }
            catch (Exception e)
            {
                ErrorHandlerHelper.LogError(e, GetType());
            }

            if (grids.Count == 0)
                grids.Add(rootGrid);

            var slims = new List<IMySlimBlock>();
            for (var g = 0; g < grids.Count; g++)
            {
                var grid = grids[g];
                if (grid == null) continue;

                slims.Clear();
                grid.GetBlocks(slims);

                for (var i = 0; i < slims.Count; i++)
                {
                    var tank = slims[i].FatBlock as IMyGasTank;
                    if (tank == null) continue;

                    var terminal = tank as IMyTerminalBlock;
                    if (terminal == null) continue;

                    if (!string.IsNullOrEmpty(token))
                    {
                        var customName = terminal.CustomName ?? string.Empty;
                        if (customName.IndexOf(token, StringComparison.OrdinalIgnoreCase) < 0)
                            continue;
                    }

                    float ratio;
                    try
                    {
                        ratio = (float)tank.FilledRatio;
                    }
                    catch (Exception e)
                    {
                        ErrorHandlerHelper.LogError(e, GetType());
                        continue;
                    }

                    var tankName = terminal.CustomName;
                    if (string.IsNullOrEmpty(tankName)) tankName = terminal.DisplayNameText;
                    if (string.IsNullOrEmpty(tankName)) tankName = terminal.BlockDefinition.SubtypeName;
                    if (string.IsNullOrEmpty(tankName)) tankName = "Gas Tank";

                    var gasSubtype = GetStoredGasSubtype(terminal);
                    var gasName = GetGasDisplayNameCached(gasSubtype);

                    var displayName = string.IsNullOrEmpty(gasName) ? tankName : gasName + " - " + tankName;
                    entries.Add(new Entry
                    {
                        Name = displayName,
                        Percentage = ratio
                    });
                }
            }
        }

        protected override void SortEntries(List<Entry> entries)
        {
            entries.Sort((a, b) =>
            {
                var cmp = b.Percentage.CompareTo(a.Percentage);
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
            return entry.Percentage;
        }

        protected override Color? GetEntryUsageColor(float pct)
        {
            if (pct <= .10f)
                return AppConfig.ErrorColor;
            if (pct <= .25f)
                return AppConfig.WarningColor;
            return null;
        }

        string GetStoredGasSubtype(IMyTerminalBlock tank)
        {
            try
            {
                var defBase = MyDefinitionManager.Static.GetCubeBlockDefinition(tank.BlockDefinition);
                var gasDef = defBase as MyGasTankDefinition;
                if (gasDef != null && !string.IsNullOrEmpty(gasDef.StoredGasId.SubtypeName))
                    return gasDef.StoredGasId.SubtypeName;
            }
            catch (Exception e)
            {
                ErrorHandlerHelper.LogError(e, GetType());
            }

            return string.Empty;
        }

        string GetGasDisplayNameCached(string subtype)
        {
            if (string.IsNullOrEmpty(subtype))
                return string.Empty;

            string display;
            if (_gasDisplayNameCache.TryGetValue(subtype, out display))
                return display;

            display = GetGasDisplayName(subtype);
            _gasDisplayNameCache[subtype] = display;
            return display;
        }

        string GetGasDisplayName(string subtype)
        {
            try
            {
                var id = new MyDefinitionId(typeof(MyObjectBuilder_GasProperties), subtype);

                MyGasProperties def;
                if (MyDefinitionManager.Static.TryGetDefinition(id, out def))
                {
                    var s = def.DisplayNameString;
                    if (!string.IsNullOrEmpty(s))
                        return s;

                    if (def.DisplayNameEnum.HasValue)
                    {
                        var sb = MyTexts.Get(def.DisplayNameEnum.Value);
                        if (sb != null)
                        {
                            s = sb.ToString();
                            if (!string.IsNullOrEmpty(s))
                                return s;
                        }
                    }

                    if (!string.IsNullOrEmpty(def.DisplayNameText))
                        return def.DisplayNameText;
                }
            }
            catch (Exception e)
            {
                ErrorHandlerHelper.LogError(e, GetType());
            }

            return subtype;
        }

        public class Entry
        {
            public string Name;
            public float Percentage;
        }
    }
}