using LcdMod.Common.Config.Components;
using System;
using System.Collections.Generic;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.Apps.Gas;
using LcdMod.Client.Extensions;
using LcdMod.Client.GridData;
using LcdMod.Client.Gui.ControlsTemplates;
using LcdMod.Client.Helpers;
using LcdMod.Client.Terminal.Controls;
using LcdMod.Common.Helpers;
using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage;
using VRage.Game;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI;
using VRage.Game.ObjectBuilders.Definitions;
using VRageMath;
using IMyTerminalBlock = Sandbox.ModAPI.IMyTerminalBlock;

using LcdMod.Common.Config.Generation;
namespace LcdMod.Client.Apps
{
    [LcdApp(3)]
    [ConfigComponent(Constants.BLOCKS, typeof(BlockSelectionConfigComponent), PropertyName = "BlockSelectionComponent")]
    public sealed partial class GasApp : App, IGasHost
    {

        readonly Dictionary<string, string> _gasDisplayNameCache =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        readonly List<GasEntry> _entries = new List<GasEntry>();
        readonly GasViewModel _viewModel;
        readonly LinkedTypedBlockSourceSet<IMyGasTank> _tanks =
            new LinkedTypedBlockSourceSet<IMyGasTank>(delegate(TypedBlockCollection blocks)
            {
                return blocks.GasTanks;
            });

        public bool HasEntries => _entries.Count > 0;
        internal IReadOnlyList<GasEntry> Entries => _entries;
        internal bool NativeGridMode => GeneralComponent.DisplayMode == (int)DisplayMode.Grid;
        internal override float WindowRenderScaling => CardScale;
        internal string NativeEntryClasses => (NativeGridMode ? "card" : "row") + (GeneralComponent.DrawLines ? " lines" : string.Empty);

        IReadOnlyList<GasEntry> IGasHost.Entries => Entries;
        bool IGasHost.NativeGridMode => NativeGridMode;
        string IGasHost.NativeEntryClasses => NativeEntryClasses;
        Color IGasHost.NativeFill(float percentage) => NativeFill(percentage);

        // The usage color while the entry is critical, otherwise the card fill; readable against what is behind it.
        internal Color NativeFill(float percentage)
        {
            bool lines = GeneralComponent.DrawLines;
            Color behind = lines ? Host.BackgroundColor : GetHeaderColor();
            Color fill = GetEntryUsageColor(percentage) ??
                         ThemeColor(lines ? LcdMod.Client.Gui.Styling.ThemeResources.AccentColor
                             : LcdMod.Client.Gui.Styling.ThemeResources.CardFillColor);
            return fill.EnsureMinimalContrast(behind);
        }

        public GasApp(IAppHost host) : base(host)
        {
            _viewModel = new GasViewModel(this);
            SetContent(new GasView { DataContext = _viewModel });
        }

        public override void Update()
        {
            _entries.Clear();
            ReadEntries(_entries);
            _entries.Sort((a, b) =>
            {
                var cmp = b.Percentage.CompareTo(a.Percentage);
                if (cmp != 0) return cmp;
                return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            });
            _viewModel.Refresh();
        }

        public override List<MySprite> GetSprites()
        {
            return new List<MySprite>();
        }

        public override void Close()
        {
            _tanks.Dispose();
            base.Close();
        }

        Color? GetEntryUsageColor(float pct)
        {
            if (pct <= .10f)
                return ColorComponent.ResolveErrorColor();
            if (pct <= .25f)
                return ColorComponent.ResolveWarningColor();
            return null;
        }

        void ReadEntries(List<GasEntry> entries)
        {
            ReadEntries(Host.GridLogic, Host.Block as IMyTerminalBlock, entries, Host.GetType());
        }

        void ReadEntries(GridLogic gridLogic, IMyTerminalBlock sourceBlock, List<GasEntry> entries, Type logType)
        {
            string mode;
            string token;
            ParseFilter(sourceBlock, out mode, out token);

            if (gridLogic == null)
                return;

            _tanks.Bind(gridLogic, (GridLinkTypeEnum)BlockSelectionComponent.GridLinkTypeInternal);
            var sources = _tanks.Sources;
            for (var sourceIndex = 0; sourceIndex < sources.Count; sourceIndex++)
            {
                var tanks = sources[sourceIndex];
                for (var i = 0; i < tanks.Count; i++)
                {
                    var tank = tanks[i];
                    if (tank == null)
                        continue;

                    var terminal = (IMyTerminalBlock)tank;

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
                    var cubeBlock = terminal as MyCubeBlock;
                    var icon = cubeBlock == null || cubeBlock.BlockDefinition == null
                        ? "MissingIcon"
                        : TextureHelper.GetOrAddTextureForBlock(cubeBlock.BlockDefinition);

                    entries.Add(new GasEntry
                    {
                        EntryId = terminal.EntityId,
                        Icon = icon,
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
