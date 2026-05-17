using System;
using System.Collections.Generic;
using Sandbox.Game.GameSystems.TextSurfaceScripts;
using Sandbox.ModAPI;
using VRageMath;
using IMyCubeBlock = VRage.Game.ModAPI.IMyCubeBlock;
using IMyTerminalBlock = Sandbox.ModAPI.IMyTerminalBlock;

namespace LcdMod.Client.Apps
{
    [MyTextSurfaceScript(ID, TITLE)]
    public partial class GasSurfaceScript : Abstract.PercentageSurfaceScript<GasSurfaceScript.Entry>
    {
        public const string ID = "GasGraph";
        public const string TITLE = "LcdMod_GasFilled";

        readonly GasApp _app = new GasApp();

        public GasSurfaceScript(IMyTextSurface surface, IMyCubeBlock block, Vector2 size) : base(surface, block, size)
        {
        }

        protected override string DefaultTitle => TITLE;

        protected override int GetMaxColsFromSurface() => 1;

        protected override void ReadEntries(List<Entry> entries)
        {
            _app.ReadEntries(Block as IMyTerminalBlock, entries, GetType());
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

        public class Entry
        {
            public string Name;
            public float Percentage;
        }
    }
}
