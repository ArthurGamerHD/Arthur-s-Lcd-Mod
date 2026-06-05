using System;
using System.Collections.Generic;
using System.Text;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.Extensions;
using LcdMod.Client.Grid;
using LcdMod.Client.Gui;
using LcdMod.Client.Gui.ControlsTemplates;
using LcdMod.Client.Gui.ControlsTemplates.Panels;
using LcdMod.Client.Gui.ControlsTemplates.Panels.WrapPanel;
using LcdMod.Client.Gui.ControlsTemplates.Progress;
using LcdMod.Client.Helpers;
using LcdMod.Client.Terminal.Controls;
using LcdMod.Common.Config.Models.Apps;
using LcdMod.Common.Helpers;
using Sandbox.Definitions;
using Sandbox.ModAPI;
using VRage;
using VRage.Game;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI;
using VRage.Game.ObjectBuilders.Definitions;
using VRageMath;
using IMyTerminalBlock = Sandbox.ModAPI.IMyTerminalBlock;
using VisualStackPanel = LcdMod.Client.Gui.ControlsTemplates.Panels.StackPanel.StackPanel;
using VisualWrapPanel = LcdMod.Client.Gui.ControlsTemplates.Panels.WrapPanel.WrapPanel;

namespace LcdMod.Client.Apps
{
    public sealed class GasApp : AppBase
    {
        const int LINE_HEIGHT = 40;
        const int SCROLL_DELAY = 12;

        readonly Dictionary<string, string> _gasDisplayNameCache =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        readonly List<Entry> _entries = new List<Entry>();
        readonly List<RectangleControl> _entryControls = new List<RectangleControl>();
        readonly ScrollPanel _scrollPanel;
        readonly VisualStackPanel _listPanel;
        readonly VisualWrapPanel _gridPanel;

        ScreenConfigWithBlocks Config => (ScreenConfigWithBlocks)AppConfig;
        public bool HasEntries => _entries.Count > 0;

        public GasApp(ScreenConfigWithBlocks config, IAppHost host) : base(config, host)
        {
            _scrollPanel = new ScrollPanel(CursorType.Default, this);
            _scrollPanel.SetVisible(false);
            _listPanel = new VisualStackPanel();
            _gridPanel = new VisualWrapPanel();
            _gridPanel.CustomRender = RenderGridPanelContent;
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
        }

        public override List<MySprite> GetSprites()
        {
            var sprites = new List<MySprite>();
            ClearControls();
            switch (Config.DisplayMode)
            {
                case (int)DisplayMode.Grid:
                    DrawGrid(sprites);
                    break;
                default:
                    DrawList(sprites);
                    break;
            }

            return sprites;
        }

        void DrawList(List<MySprite> sprites)
        {
            if (_entries.Count <= 0)
                return;

            var rowHeight = LINE_HEIGHT * Host.Scale;
            _scrollPanel.SetContent(_listPanel);
            _listPanel.RowHeight = rowHeight;
            _listPanel.Gap = 0f;
            SyncPanelChildren(_listPanel, false);
            ConfigureScrollPanel(rowHeight);

            var renderContext = CreateRenderContext();
            _scrollPanel.Render(renderContext, sprites);
        }

        void DrawGrid(List<MySprite> sprites)
        {
            if (_entries.Count <= 0)
                return;

            var rowHeight = 2f * LINE_HEIGHT * Host.Scale;
            _scrollPanel.SetContent(_gridPanel);
            _gridPanel.RowHeight = rowHeight;
            _gridPanel.MinimumColumnWidth = Host.ViewBox.Width + 1f;
            _gridPanel.ForceSingleColumn = true;
            _gridPanel.HorizontalGap = 0f;
            _gridPanel.VerticalGap = 0f;
            SyncPanelChildren(_gridPanel, true);
            ConfigureScrollPanel(rowHeight);

            var renderContext = CreateRenderContext();
            _scrollPanel.Render(renderContext, sprites);
        }

        void ClearControls()
        {
            _scrollPanel.SetVisible(false);
            for (int i = 0; i < _entryControls.Count; i++)
            {
                if (_entryControls[i] != null)
                    _entryControls[i].SetVisible(false);
            }
        }

        void ConfigureScrollPanel(float rowHeight)
        {
            var contentTop = GetContentTop();
            var viewportHeight = Math.Max(0f, Host.ViewBox.Bottom - contentTop);
            _scrollPanel.ConfigureAutomatic(
                new RectangleF(Host.ViewBox.X, contentTop, Host.ViewBox.Width, viewportHeight),
                ScrollPanel.DefaultScrollerWidthPixels * Host.Scale,
                rowHeight,
                SCROLL_DELAY / 6f);
            _scrollPanel.SetScrollBarColors(
                new Color(Host.Surface.ScriptForegroundColor.R, Host.Surface.ScriptForegroundColor.G, Host.Surface.ScriptForegroundColor.B, 127),
                new Color(Config.HeaderColor.R, Config.HeaderColor.G, Config.HeaderColor.B, 250));
            _scrollPanel.SetVisible(true);
        }

        ControlRenderContext CreateRenderContext()
        {
            return CreateControlRenderContext(
                Host.Surface,
                Host.Scale,
                Host.Surface.FontSize,
                new Vector2(float.NaN, float.NaN));
        }

        void SyncPanelChildren(Panel panel, bool renderAsGrid)
        {
            if (panel == null)
                return;

            EnsureEntryControlCount(_entries.Count);
            RemoveExtraPanelChildren(panel, _entries.Count);

            var children = panel.Children;
            bool changed = false;
            for (int i = 0; i < _entries.Count; i++)
            {
                var control = _entryControls[i];
                control.SetDataContext(_entries[i]);
                control.CustomRender = renderAsGrid ? RenderGridEntryControl : (InteractiveRenderHandler)RenderListEntryControl;
                control.SetVisible(true);

                if (!ReferenceEquals(control.Parent, panel))
                {
                    panel.AddChild(control);
                    children = panel.Children;
                    changed = true;
                }

                if (children == null || i >= children.Count || ReferenceEquals(children[i], control))
                    continue;

                int currentIndex = IndexOfChild(children, control);
                if (currentIndex < 0)
                    continue;

                if (panel.MoveChild(control, i))
                    changed = true;
            }

            if (changed)
                panel.InvalidateLayout();
        }

        void EnsureEntryControlCount(int count)
        {
            while (_entryControls.Count < count)
            {
                _entryControls.Add(new RectangleControl(default(RectangleF), CursorType.Default)
                {
                    CustomRender = RenderListEntryControl
                });
            }
        }

        void RemoveExtraPanelChildren(Panel panel, int desiredCount)
        {
            var children = panel.Children;
            if (children == null)
                return;

            for (int i = children.Count - 1; i >= desiredCount; i--)
                panel.RemoveChild(children[i]);
        }

        static int IndexOfChild(IReadOnlyList<ControlBase> children, ControlBase child)
        {
            if (children == null || child == null)
                return -1;

            for (int i = 0; i < children.Count; i++)
            {
                if (ReferenceEquals(children[i], child))
                    return i;
            }

            return -1;
        }

        void RenderListEntryControl(ControlBase control, ControlRenderContext context, List<MySprite> frame)
        {
            var entry = control != null ? control.DataContext as Entry : null;
            if (entry == null)
                return;

            DrawRow(frame, entry, control.Bounds);
        }

        void RenderGridEntryControl(ControlBase control, ControlRenderContext context, List<MySprite> frame)
        {
            var entry = control != null ? control.DataContext as Entry : null;
            if (entry == null)
                return;

            DrawGridCell(frame, entry, control.Bounds.X, control.Bounds.Right, control.Bounds.Y, control.Bounds.Height);
        }

        void RenderGridPanelContent(ControlBase control, ControlRenderContext context, List<MySprite> sprites)
        {
            var children = control != null ? control.Children : null;
            if (children == null)
                return;

            if (Config.DrawLines)
            {
                var layout = WrapPanelLayout.Create(
                    control.Bounds,
                    _gridPanel.RowHeight,
                    _gridPanel.MinimumColumnWidth,
                    children.Count,
                    0,
                    true);
                DrawGridLines(sprites, layout);
            }

            for (int i = 0; i < children.Count; i++)
            {
                var child = children[i];
                if (child != null)
                    child.Render(context, sprites);
            }
        }

        void DrawGridLines(List<MySprite> sprites, WrapPanelLayout layout)
        {
            var lineColor = Config.HeaderColor;
            var contentStart = _scrollPanel.ContentBounds.X;
            var contentEnd = _scrollPanel.ContentBounds.Right;
            var gridHeight = _scrollPanel.ContentBounds.Height;

            for (int row = 0; row <= _scrollPanel.MaxVisibleRows; row++)
            {
                var y = _scrollPanel.ContentBounds.Y + row * layout.RowHeight;
                sprites.Add(new MySprite { Type = SpriteType.TEXTURE, Data = "SquareSimple", Position = new Vector2((contentStart + contentEnd) / 2f, y), Size = new Vector2(contentEnd - contentStart, 2f), Color = lineColor, Alignment = TextAlignment.CENTER });
            }

            var lineCenterY = _scrollPanel.ContentViewportBounds.Y + gridHeight / 2f;
            sprites.Add(new MySprite { Type = SpriteType.TEXTURE, Data = "SquareSimple", Position = new Vector2(contentStart, lineCenterY), Size = new Vector2(2f, gridHeight), Color = lineColor, Alignment = TextAlignment.CENTER });
            sprites.Add(new MySprite { Type = SpriteType.TEXTURE, Data = "SquareSimple", Position = new Vector2(contentEnd, lineCenterY), Size = new Vector2(2f, gridHeight), Color = lineColor, Alignment = TextAlignment.CENTER });
        }

        void DrawRow(List<MySprite> frame, Entry entry, RectangleF bounds)
        {
            var pct = MathHelper.Clamp(entry.Percentage, 0f, 1f);
            Vector2 position = bounds.Position;

            if (Config.DrawLines)
                frame.Add(new MySprite { Type = SpriteType.TEXTURE, Data = "SquareSimple", Position = new Vector2(bounds.Center.X, position.Y), Size = new Vector2(bounds.Width, 2f), Color = Host.ForegroundColor, Alignment = TextAlignment.CENTER });

            var barMargin = 8 * Host.Scale;
            Vector2 size = new Vector2(bounds.Width, bounds.Height) - barMargin;
            var rowClip = new RectangleF(
                bounds.X,
                bounds.Y,
                Math.Max(0f, bounds.Width - 145f * Host.Scale),
                bounds.Height);

            if (BeginNestedClip(frame, rowClip))
            {
                var activeRowClip = Intersect(rowClip, _scrollPanel.ContentViewportBounds);
                DrawClippedProgressBar(
                    frame,
                    new Vector2(position.X, position.Y + Host.Scale) + barMargin / 2f,
                    size,
                    pct,
                    activeRowClip);
                position.X += 16 * Host.Scale;
                position.Y += 4 * Host.Scale;
                frame.Add(new MySprite { Type = SpriteType.TEXT, Data = entry.Name, Position = position, RotationOrScale = Host.Scale, Color = Host.Surface.ScriptForegroundColor, Alignment = TextAlignment.LEFT, FontId = "White" });
                EndNestedClipAndRestoreScrollClip(frame);
            }

            position.X = bounds.Right;
            frame.Add(new MySprite { Type = SpriteType.TEXT, Data = FormatingHelper.PercentageToString(pct), Position = position, RotationOrScale = Host.Scale, Color = Host.Surface.ScriptForegroundColor, Alignment = TextAlignment.RIGHT, FontId = "White" });
        }

        void DrawClippedProgressBar(
            List<MySprite> frame,
            Vector2 topLeft,
            Vector2 size,
            float pct,
            RectangleF rowClip)
        {
            var bgColor = Host.BackgroundColor.DeriveAccentColor();
            var fillColor = Config.HeaderColor;
            var fillOverride = GetEntryUsageColor(pct);

            BarPanel.CreateSprites(frame, topLeft, size, fillColor, bgColor, 0f);

            var fillWidth = MathHelper.Clamp(pct, 0f, 1f) * Math.Max(1f, size.X);
            if (fillWidth <= 0.001f)
                return;

            var fillClip = Intersect(
                new RectangleF(topLeft.X, topLeft.Y, fillWidth, Math.Max(1f, size.Y)),
                rowClip);
            if (fillClip.Width <= 0f || fillClip.Height <= 0f)
                return;

            AddClip(frame, fillClip);
            BarPanel.CreateSprites(frame, topLeft, size, fillColor, bgColor, 1f, fillOverride);
            EndNestedClipAndRestoreClip(frame, rowClip);
        }

        bool BeginNestedClip(List<MySprite> sprites, RectangleF bounds)
        {
            var clip = Intersect(bounds, _scrollPanel.ContentViewportBounds);
            if (clip.Width <= 0f || clip.Height <= 0f)
                return false;

            AddClip(sprites, clip);
            return true;
        }

        void EndNestedClipAndRestoreScrollClip(List<MySprite> sprites)
        {
            if (sprites == null)
                return;

            sprites.Add(MySprite.CreateClearClipRect());
            AddClip(sprites, _scrollPanel.ContentViewportBounds);
        }

        static void EndNestedClipAndRestoreClip(List<MySprite> sprites, RectangleF clip)
        {
            if (sprites == null)
                return;

            sprites.Add(MySprite.CreateClearClipRect());
            AddClip(sprites, clip);
        }

        static RectangleF Intersect(RectangleF a, RectangleF b)
        {
            float x = Math.Max(a.X, b.X);
            float y = Math.Max(a.Y, b.Y);
            float right = Math.Min(a.Right, b.Right);
            float bottom = Math.Min(a.Bottom, b.Bottom);
            return new RectangleF(x, y, Math.Max(0f, right - x), Math.Max(0f, bottom - y));
        }

        static void AddClip(List<MySprite> sprites, RectangleF bounds)
        {
            if (sprites == null)
                return;

            int x = (int)Math.Floor(bounds.X);
            int y = (int)Math.Floor(bounds.Y);
            int right = (int)Math.Ceiling(bounds.Right);
            int bottom = (int)Math.Ceiling(bounds.Bottom);
            sprites.Add(MySprite.CreateClipRect(new Rectangle(x, y, Math.Max(0, right - x), Math.Max(0, bottom - y))));
        }

        void DrawGridCell(List<MySprite> frame, Entry entry, float xStart, float xEnd, float yStart, float rowHeight)
        {
            var cellPadding = (LINE_HEIGHT * Host.Scale) / 3f;
            var pct = MathHelper.Clamp(entry.Percentage, 0f, 1f);
            var cellView = GetCellViewBox(xStart, xEnd, yStart, rowHeight, cellPadding);

            if (!Config.DrawLines)
            {
                var backgroundColor = Config.HeaderColor;
                var hsv = VRageMath.ColorExtensions.ColorToHSV(backgroundColor);
                hsv.Z *= 0.2f;
                var cellRect = new RectangleF(xStart + cellPadding / 2f, yStart + cellPadding / 2f, (xEnd - xStart) - cellPadding, rowHeight - cellPadding);
                var dropShadow = new RectangleF(cellRect.Position + 2, cellRect.Size);
                Border.CreateSpritesFromRect(dropShadow, frame, hsv.HSVtoColor(),
                    radiusScale: Host.Scale);
                Border.CreateSpritesFromRect(cellRect, frame, backgroundColor,
                    radiusScale: Host.Scale);
            }

            var nameHeight = Math.Max(0f, cellView.Height * .45f);
            var nameRect = new RectangleF(cellView.X, cellView.Y, cellView.Width, nameHeight);
            var bottomRect = new RectangleF(cellView.X, nameRect.Bottom, cellView.Width, Math.Max(0f, cellView.Bottom - nameRect.Bottom));
            var name = new StringBuilder(entry.Name ?? string.Empty);
            TrimText(ref name, nameRect.Width);
            frame.Add(new MySprite { Type = SpriteType.TEXT, Data = name.ToString(), Position = new Vector2(nameRect.X + 2f * Host.Scale, nameRect.Y + 2f * Host.Scale), RotationOrScale = .9f * Host.Scale, Color = Host.Surface.ScriptForegroundColor, Alignment = TextAlignment.LEFT, FontId = "White" });

            var barWidth = bottomRect.Width * (2f / 3f);
            var textRect = new RectangleF(bottomRect.X + barWidth, bottomRect.Y, bottomRect.Width - barWidth, bottomRect.Height);
            var barRect = new RectangleF(bottomRect.X, bottomRect.Y, barWidth, bottomRect.Height);
            var barInnerPaddingX = 2f * Host.Scale;
            var barInnerPaddingY = bottomRect.Height * 0.2f;
            var fillColor = Extensions.ColorExtensions.DeriveAccentColor(Config.HeaderColor, .4f, 0.5);
            BarPanel.CreateSprites(frame, new Vector2(barRect.X + barInnerPaddingX, barRect.Y + barInnerPaddingY + (2f * Host.Scale)), new Vector2(Math.Max(1f, barRect.Width - 2f * barInnerPaddingX), Math.Max(1f, barRect.Height - 2f * barInnerPaddingY)), fillColor, fillColor.DeriveAccentColor(.6f, 0.7), pct, GetEntryUsageColor(pct));
            frame.Add(new MySprite { Type = SpriteType.TEXT, Data = FormatingHelper.PercentageToString(pct), Position = new Vector2(textRect.Right - (2f * Host.Scale), textRect.Y + 2f * Host.Scale), RotationOrScale = .95f * Host.Scale, Color = Host.Surface.ScriptForegroundColor, Alignment = TextAlignment.RIGHT, FontId = "White" });
        }

        Color? GetEntryUsageColor(float pct)
        {
            if (pct <= .10f)
                return Config.ErrorColor;
            if (pct <= .25f)
                return Config.WarningColor;
            return null;
        }

        float GetContentTop()
        {
            return Host.TitleVisible ? Host.ViewBox.Y + (40f * Host.Scale * Host.Surface.FontSize) : Host.ViewBox.Y;
        }

        RectangleF GetCellViewBox(float xStart, float xEnd, float yStart, float cellHeight, float cellPadding)
        {
            var innerLeft = xStart + cellPadding;
            var innerRight = xEnd - cellPadding;
            var innerTop = yStart + cellPadding;
            var innerBottom = yStart + cellHeight - cellPadding;
            return new RectangleF(innerLeft, innerTop, innerRight - innerLeft, innerBottom - innerTop);
        }

        void TrimText(ref StringBuilder sb, float availableWidth, float fontSize = 1f)
        {
            Vector2 textSize = Host.Surface.MeasureStringInPixels(sb, "White", fontSize * Host.Scale);
            if (textSize.X <= availableWidth)
                return;

            var source = sb.ToString();
            for (int i = source.Length - 1; i > 0; i--)
            {
                sb.Clear();
                sb.Append(FormatingHelper.TrimName(source, i));
                textSize = Host.Surface.MeasureStringInPixels(sb, "White", fontSize * Host.Scale);
                if (textSize.X <= availableWidth)
                    break;
            }
        }

        void ReadEntries(List<Entry> entries)
        {
            ReadEntries(Host.GridLogic, Host.Block as IMyTerminalBlock, entries, Host.GetType());
        }

        void ReadEntries(GridLogic gridLogic, IMyTerminalBlock sourceBlock, List<Entry> entries, Type logType)
        {
            string mode;
            string token;
            ParseFilter(sourceBlock, out mode, out token);

            if (gridLogic == null)
                return;

            var tanks = gridLogic.GetTerminalBlocks<IMyGasTank>(Config.GridLinkType);
            if (tanks == null)
                return;

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

                entries.Add(new Entry
                {
                    Name = displayName,
                    Percentage = ratio
                });
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

        public class Entry
        {
            public string Name;
            public float Percentage;
        }
    }
}
