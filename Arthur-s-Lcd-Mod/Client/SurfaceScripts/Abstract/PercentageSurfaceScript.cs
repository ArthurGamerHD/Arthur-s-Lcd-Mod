using System.Collections.Generic;
using Generated;
using LcdMod.Client.Extensions;
using LcdMod.Client.Helpers;
using LcdMod.Client.Terminal.Controls;
using LcdMod.Client.Utility;
using Sandbox.ModAPI;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRageMath;

namespace LcdMod.Client.SurfaceScripts.Abstract
{
    public abstract partial class PercentageSurfaceScript<TEntry> : SurfaceScriptBase, IMultiDisplayMode
    {
        protected override ConfigKind ConfigKind => ConfigKind.General;
        protected const int LINE_HEIGHT = 40;
        protected const int MINIMUM_COL_WIDTH = 220;
        protected const int SCROLL_DELAY = 12;

        readonly PercentageApp<TEntry> _app;
        Color _scriptForegroundColor;

        protected PercentageSurfaceScript(IMyTextSurface surface, IMyCubeBlock block, Vector2 size) : base(surface, block, size)
        {
            _app = new PercentageApp<TEntry>(this);
        }

        public List<MyTerminalControlComboBoxItem> GetDisplayModes() => DisplayModes.GridAndLegacy;

        public override void SafeRun()
        {
            if (AppConfig == null)
                return;

            if (_scriptForegroundColor != Surface.ScriptForegroundColor)
                LayoutChanged();

            UpdateViewBox();
            RenderSprites();
        }

        public override List<MySprite> GetSprites()
        {
            var sprites = _app.GetSprites();
            if (sprites == null)
            {
                sprites = new List<MySprite>();
                AddEmptySprites(sprites);
            }

            return sprites;
        }

        protected override void LayoutChanged()
        {
            _scriptForegroundColor = Surface.ScriptForegroundColor;
            base.LayoutChanged();
        }

        protected abstract void ReadEntries(List<TEntry> entries);
        protected virtual void SortEntries(List<TEntry> entries) { }
        protected abstract string GetEntryName(TEntry entry);
        protected abstract float GetEntryPercentage(TEntry entry);

        protected virtual Color? GetEntryUsageColor(float pct) => null;
        protected virtual string GetNumber(float pct) => FormatingHelper.PercentageToString(pct);
        protected virtual Color GetEntryBarFillColor() => AppConfig.HeaderColor;
        protected virtual Color GetEntryBarBackgroundColor() => BackgroundColor.DeriveAccentColor();

        protected virtual int GetMaxColsFromSurface()
        {
            var max = ViewBox.Width - ViewBox.X;
            var perCol = MINIMUM_COL_WIDTH * ConfiguredScale;
            return (int)System.Math.Max(1, System.Math.Round(max / perCol - .5, System.MidpointRounding.AwayFromZero));
        }

        internal void ReadEntriesInternal(List<TEntry> entries) => ReadEntries(entries);
        internal void SortEntriesInternal(List<TEntry> entries) => SortEntries(entries);
        internal string GetEntryNameInternal(TEntry entry) => GetEntryName(entry);
        internal float GetEntryPercentageInternal(TEntry entry) => GetEntryPercentage(entry);
        internal Color? GetEntryUsageColorInternal(float pct) => GetEntryUsageColor(pct);
        internal string GetNumberInternal(float pct) => GetNumber(pct);
        internal Color GetEntryBarFillColorInternal() => GetEntryBarFillColor();
        internal Color GetEntryBarBackgroundColorInternal() => GetEntryBarBackgroundColor();
        internal int GetMaxColsFromSurfaceInternal() => GetMaxColsFromSurface();

        internal void AddBackgroundInternal(List<MySprite> sprites) => AddBackground(sprites);
        internal void DrawTitleInternal(List<MySprite> sprites) => DrawTitle(sprites);
        internal float CaretYInternal
        {
            get { return CaretY; }
            set { CaretY = value; }
        }
        internal float FooterHeightInternal => FooterHeight;
        internal Sandbox.ModAPI.Ingame.IMyTextSurface SurfaceInternal => Surface;
        internal Color ForegroundColorInternal => ForegroundColor;
        internal RectangleF GetCellViewBoxInternal(float xStart, float xEnd, float yStart, float cellHeight, float cellPadding) =>
            GetCellViewBox(xStart, xEnd, yStart, cellHeight, cellPadding);
        internal void TrimTextInternal(ref System.Text.StringBuilder sb, float availableWidth, float fontSize = 1f) =>
            TrimText(ref sb, availableWidth, fontSize);
        internal int DisplayModeInternal => AppConfig.DisplayMode;
        internal bool DrawLinesInternal => AppConfig.DrawLines;
        internal Color HeaderColorInternal => AppConfig.HeaderColor;
        internal float FontScaleInternal => FontScale;
    }
}
