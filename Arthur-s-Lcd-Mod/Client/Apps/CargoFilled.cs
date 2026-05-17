using System;
using System.Collections.Generic;
using Generated;
using LcdMod.Client.Terminal.Controls.Groups;
using Sandbox.Game.GameSystems.TextSurfaceScripts;
using Sandbox.ModAPI;
using VRageMath;
using IMyCubeBlock = VRage.Game.ModAPI.IMyCubeBlock;
using LabelSeparator = LcdMod.Client.Terminal.Controls.Filter.LabelSeparator;
using SeparatorFilter = LcdMod.Client.Terminal.Controls.Filter.SeparatorFilter;
using ScreenConfigWithBlocks = LcdMod.Common.Config.Models.Apps.ScreenConfigWithBlocks;
using SwitchToggleLines = LcdMod.Client.Terminal.Controls.Generic.SwitchToggleLines;

namespace LcdMod.Client.Apps
{
    [MyTextSurfaceScript(ID, TITLE)]
    public partial class CargoFilledSurfaceScript : Abstract.PercentageSurfaceScript<CargoFilledSurfaceScript.Entry>,
        IUsesTerminalControl<SwitchToggleLines>,
        IUsesTerminalControl<SeparatorFilter>,
        IUsesTerminalControl<LabelSeparator>,
        IUsesTerminalControlGroup<BlocksFilterTerminalControlGroup>
    {
        protected override ConfigKind ConfigKind => ConfigKind.WithBlocks;
        readonly CargoFilledApp _app;

        public const string ID = "ContainerCharts";
        public const string TITLE = "DisplayName_CargoFilledEntityComponent";

        public CargoFilledSurfaceScript(IMyTextSurface surface, IMyCubeBlock block, Vector2 size) : base(surface, block,
            size)
        {
            _app = new CargoFilledApp(this);
        }

        protected override string DefaultTitle => TITLE;
        internal ScreenConfigWithBlocks BlocksConfig => AppConfig;

        protected override void ReadEntries(List<Entry> entries)
        {
            _app.ReadEntries(entries);
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
                return AppConfig.ErrorColor;
            if (pct > .90f)
                return AppConfig.WarningColor;
            return null;
        }

        public class Entry
        {
            public double Cap;
            public string Name;
            public double Used;
        }
    }
}
