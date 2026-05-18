using System;
using System.Collections.Generic;
using LcdMod.Common.Helpers;
using LcdMod.Client.SurfaceScripts;
using Sandbox.Definitions;
using Sandbox.ModAPI;
using VRage;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.Game.ObjectBuilders.Definitions;
using IMyCubeGrid = VRage.Game.ModAPI.IMyCubeGrid;
using IMySlimBlock = VRage.Game.ModAPI.IMySlimBlock;
using IMyTerminalBlock = Sandbox.ModAPI.IMyTerminalBlock;

namespace LcdMod.Client.Apps
{
    public sealed class GasApp
    {
        readonly Dictionary<string, string> _gasDisplayNameCache =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public void ReadEntries(IMyTerminalBlock sourceBlock, List<GasSurfaceScript.Entry> entries, Type logType)
        {
            string mode;
            string token;
            ParseFilter(sourceBlock, out mode, out token);

            var rootGrid = sourceBlock?.CubeGrid;
            if (rootGrid == null)
                return;

            var grids = new List<IMyCubeGrid>();
            try
            {
                MyAPIGateway.GridGroups.GetGroup(rootGrid, GridLinkTypeEnum.Logical, grids);
            }
            catch (Exception e)
            {
                ErrorHandlerHelper.LogError(e, logType);
            }

            if (grids.Count == 0)
                grids.Add(rootGrid);

            var slims = new List<IMySlimBlock>();
            for (var g = 0; g < grids.Count; g++)
            {
                var grid = grids[g];
                if (grid == null)
                    continue;

                slims.Clear();
                grid.GetBlocks(slims);

                for (var i = 0; i < slims.Count; i++)
                {
                    var tank = slims[i].FatBlock as IMyGasTank;
                    if (tank == null)
                        continue;

                    var terminal = tank as IMyTerminalBlock;

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
                        ErrorHandlerHelper.LogError(e, logType);
                        continue;
                    }

                    var tankName = terminal.CustomName;
                    if (string.IsNullOrEmpty(tankName))
                        tankName = terminal.DisplayNameText;
                    if (string.IsNullOrEmpty(tankName))
                        tankName = terminal.BlockDefinition.SubtypeName;
                    if (string.IsNullOrEmpty(tankName))
                        tankName = "Gas Tank";

                    var gasSubtype = GetStoredGasSubtype(terminal, logType);
                    var gasName = GetGasDisplayNameCached(gasSubtype, logType);
                    var displayName = string.IsNullOrEmpty(gasName) ? tankName : gasName + " - " + tankName;

                    entries.Add(new GasSurfaceScript.Entry
                    {
                        Name = displayName,
                        Percentage = ratio
                    });
                }
            }
        }

        static string GetStoredGasSubtype(IMyTerminalBlock tank, Type logType)
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
                ErrorHandlerHelper.LogError(e, logType);
            }

            return string.Empty;
        }

        string GetGasDisplayNameCached(string subtype, Type logType)
        {
            if (string.IsNullOrEmpty(subtype))
                return string.Empty;

            string display;
            if (_gasDisplayNameCache.TryGetValue(subtype, out display))
                return display;

            display = GetGasDisplayName(subtype, logType);
            _gasDisplayNameCache[subtype] = display;
            return display;
        }

        static string GetGasDisplayName(string subtype, Type logType)
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
                ErrorHandlerHelper.LogError(e, logType);
            }

            return subtype;
        }

        static readonly System.Text.RegularExpressions.Regex RxGroup =
            new System.Text.RegularExpressions.Regex(@"\(\s*G\s*:\s*(.+?)\s*\)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        static readonly System.Text.RegularExpressions.Regex RxContainer =
            new System.Text.RegularExpressions.Regex(@"\(\s*(?!G\s*:)(.+?)\s*\)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        static void ParseFilter(IMyTerminalBlock block, out string mode, out string token)
        {
            mode = null;
            token = null;
            if (block == null)
                return;

            var name = block.CustomName ?? string.Empty;
            var mg = RxGroup.Match(name);
            if (mg.Success)
            {
                mode = "group";
                token = mg.Groups[1].Value.Trim();
                return;
            }

            var mc = RxContainer.Match(name);
            if (mc.Success)
            {
                mode = "container";
                token = mc.Groups[1].Value.Trim();
            }
        }
    }
}
