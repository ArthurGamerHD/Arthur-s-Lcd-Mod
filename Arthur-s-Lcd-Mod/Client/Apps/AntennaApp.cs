using System;
using System.Collections.Generic;
using System.Text;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.SurfaceScripts.Abstract;
using LcdMod.Client.Gui;
using LcdMod.Client.Gui.ControlsTemplates;
using LcdMod.Client.Gui.ControlsTemplates.Panels;
using LcdMod.Client.Gui.ControlsTemplates.Panels.WrapPanel;
using LcdMod.Client.Gui.UserControls.Antenna;
using LcdMod.Client.Helpers;
using LcdMod.Client.Terminal.Controls;
using VRage.Game.GUI.TextPanel;
using VRageMath;
using ScreenConfigWithBlocks = LcdMod.Common.Config.Models.Apps.ScreenConfigWithBlocks;
using VisualStackPanel = LcdMod.Client.Gui.ControlsTemplates.Panels.StackPanel.StackPanel;
using VisualWrapPanel = LcdMod.Client.Gui.ControlsTemplates.Panels.WrapPanel.WrapPanel;

namespace LcdMod.Client.Apps
{
    public sealed class AntennaApp : AppBase, IAppInteractive
    {
        const float LINE = 22f;
        const float MINIMUM_COL_WIDTH = 400f;
        const int SCROLL_DELAY = 12;
        const float GRID_CELL_LINES = 6f;
        const float LASER_ICON_SOURCE_SIZE = 190f;
        const float LASER_ICON_TOP_PADDING = 64f;
        const float LASER_ICON_BOTTOM_PADDING = 22f;

        public ScreenConfigWithBlocks Config => (ScreenConfigWithBlocks)AppConfig;
        readonly List<AntennaEntry> _entries = new List<AntennaEntry>();
        readonly Dictionary<long, AntennaEntry> _entryModels = new Dictionary<long, AntennaEntry>();
        readonly HashSet<long> _activeEntryIds = new HashSet<long>();
        readonly List<long> _entriesToRemove = new List<long>();
        readonly Dictionary<long, RectangleControl> _entryControls = new Dictionary<long, RectangleControl>();
        readonly List<AntennaCollector> _collectors = new List<AntennaCollector>();
        readonly List<ControlBase> _interactiveList = new List<ControlBase>();
        readonly ScrollPanel _scrollPanel;
        readonly VisualStackPanel _listPanel;
        readonly VisualWrapPanel _gridPanel;
        readonly InteractiveSurfaceScript _interactiveHost;
        bool _drawGridLineSprites;
        bool _drawGridVerticalLines;
        public bool HasEntries => _entries.Count > 0;
        public List<ControlBase> InteractiveList => _interactiveList;

        public AntennaApp(ScreenConfigWithBlocks config, IAppHost script) : base(config, script)
        {
            _interactiveHost = script as InteractiveSurfaceScript;
            if (_interactiveHost == null)
                throw new ArgumentException("AntennaApp requires an InteractiveSurfaceScript host.", "script");

            _scrollPanel = new ScrollPanel(CursorType.Default, this);
            _scrollPanel.ScrollChanged = OnScrollPanelChanged;
            _scrollPanel.SetVisible(false);
            _listPanel = new VisualStackPanel();
            _listPanel.CustomRender = RenderListPanelContent;
            _gridPanel = new VisualWrapPanel();
            _gridPanel.CustomRender = RenderGridPanelContent;
        }

        public override void Update()
        {
            if (_collectors.Count == 0)
                BuildCollectors();

            _entries.Clear();
            _activeEntryIds.Clear();
            var gridLogic = Host.GridLogic;
            if (gridLogic == null)
            {
                RemoveInactiveEntryModels();
                return;
            }

            for (int i = 0; i < _collectors.Count; i++)
                _collectors[i].Collect(gridLogic, _entries, _entryModels, _activeEntryIds);

            RemoveInactiveEntryModels();
            _entries.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
        }

        public override List<MySprite> GetSprites()
        {
            ClearInteractiveTree();
            var sprites = new List<MySprite>();

            switch (Config.DisplayMode)
            {
                case (int)DisplayMode.Grid:
                    DrawGridLike(sprites, false, Config.DrawLines, Config.DrawLines, Config.DrawLines);
                    break;
                default:
                    DrawDefaultView(sprites);
                    break;
            }

            return sprites;
        }

        void BuildCollectors()
        {
            _collectors.Add(new LaserAntennaCollector(Host));
            _collectors.Add(new RadioAntennaCollector(Host));
            _collectors.Add(new BeaconCollector(Host));
        }

        void DrawDefaultView(List<MySprite> sprites)
        {
            var rowHeight = GRID_CELL_LINES * LINE * Host.Scale;
            float caretY = GetContentTop();
            float footerHeight = GetFooterHeight();
            _scrollPanel.SetContent(_listPanel);
            _listPanel.RowHeight = rowHeight;
            _listPanel.Gap = 0f;
            SyncPanelChildren(_listPanel, _entries, true);
            ConfigureScrollPanel(caretY, footerHeight, rowHeight);

            var renderContext = CreateRenderContext();
            _scrollPanel.Render(renderContext, sprites);
        }

        void DrawGridLike(List<MySprite> sprites, bool forceSingleColumn, bool drawLineSprites, bool drawVerticalLines, bool drawCellsAsLines)
        {
            var rowHeight = GRID_CELL_LINES * LINE * Host.Scale;
            float caretY = GetContentTop();
            float footerHeight = GetFooterHeight();
            _scrollPanel.SetContent(_gridPanel);
            _gridPanel.RowHeight = rowHeight;
            _gridPanel.MinimumColumnWidth = MINIMUM_COL_WIDTH * Host.Scale;
            _gridPanel.ForceSingleColumn = forceSingleColumn;
            _gridPanel.HorizontalGap = 0f;
            _gridPanel.VerticalGap = 0f;
            _drawGridLineSprites = drawLineSprites;
            _drawGridVerticalLines = drawVerticalLines;
            SyncPanelChildren(_gridPanel, _entries, drawCellsAsLines);
            ConfigureScrollPanel(caretY, footerHeight, rowHeight);

            var renderContext = CreateRenderContext();
            _scrollPanel.Render(renderContext, sprites);
        }


        void ClearInteractiveTree()
        {
            _scrollPanel.SetVisible(false);
            _interactiveList.Clear();

            foreach (var kv in _entryControls)
                kv.Value?.SetVisible(false);
        }

        void ConfigureScrollPanel(float contentTop, float footerHeight, float rowHeight)
        {
            var viewportHeight = Math.Max(0f, Host.ViewBox.Bottom - contentTop - Math.Max(0f, footerHeight));
            _scrollPanel.ConfigureAutomatic(
                new RectangleF(Host.ViewBox.X, contentTop, Host.ViewBox.Width, viewportHeight),
                _scrollPanel.AutomaticScrollerWidthPixels * Host.Scale,
                rowHeight,
                SCROLL_DELAY / 6f);
            _scrollPanel.SetScrollBarColors(
                new Color(Host.Surface.ScriptForegroundColor.R, Host.Surface.ScriptForegroundColor.G, Host.Surface.ScriptForegroundColor.B, 127),
                new Color(Config.HeaderColor.R, Config.HeaderColor.G, Config.HeaderColor.B, 250));
            _scrollPanel.SetVisible(true);
            if (!_interactiveList.Contains(_scrollPanel))
                _interactiveList.Add(_scrollPanel);
        }

        ControlRenderContext CreateRenderContext()
        {
            return CreateControlRenderContext(
                Host.Surface,
                Host.Scale,
                Host.Surface.FontSize,
                new Vector2(float.NaN, float.NaN));
        }

        void SyncPanelChildren(Panel panel, List<AntennaEntry> entries, bool drawAsLines)
        {
            if (panel == null)
                return;

            var desired = new List<ControlBase>(entries == null ? 0 : entries.Count);
            var desiredIds = new HashSet<long>();
            if (entries != null)
            {
                for (int i = 0; i < entries.Count; i++)
                {
                    var entry = entries[i];
                    if (entry == null)
                        continue;

                    desiredIds.Add(entry.EntryId);
                    desired.Add(GetOrCreateEntryControl(entry, drawAsLines));
                }
            }

            RemoveStalePanelChildren(panel, desiredIds);
            EnsurePanelChildOrder(panel, desired);
        }

        RectangleControl GetOrCreateEntryControl(AntennaEntry dataContext, bool drawAsLines)
        {
            if (dataContext == null)
                return null;

            dataContext.DrawAsLines = drawAsLines;

            RectangleControl control;
            if (!_entryControls.TryGetValue(dataContext.EntryId, out control) || control == null)
            {
                control = new RectangleControl(default(RectangleF), CursorType.Default, dataContext)
                {
                    CustomRender = RenderAntennaEntryControl
                };
                _entryControls[dataContext.EntryId] = control;
            }
            else
            {
                control.SetDataContext(dataContext);
                control.CustomRender = RenderAntennaEntryControl;
            }

            control.SetVisible(true);
            return control;
        }

        void RenderListPanelContent(ControlBase control, ControlRenderContext context, List<MySprite> sprites)
        {
            var children = control != null ? control.Children : null;
            if (children == null)
                return;

            if (Config.DrawLines)
                DrawHorizontalLines(sprites, Host.ForegroundColor, "Circle", _listPanel.RowHeight);

            RenderPanelChildren(children, context, sprites);
        }

        void RenderGridPanelContent(ControlBase control, ControlRenderContext context, List<MySprite> sprites)
        {
            var children = control != null ? control.Children : null;
            if (children == null)
                return;

            if (_drawGridLineSprites)
            {
                var layout = WrapPanelLayout.Create(
                    control.Bounds,
                    _gridPanel.RowHeight,
                    _gridPanel.MinimumColumnWidth,
                    children.Count,
                    0,
                    _gridPanel.ForceSingleColumn);
                DrawWrapPanelLines(sprites, layout, _drawGridVerticalLines);
            }

            RenderPanelChildren(children, context, sprites);
        }

        void DrawHorizontalLines(List<MySprite> sprites, Color color, string texture, float rowHeight)
        {
            var contentStart = _scrollPanel.ContentBounds.X;
            var contentEnd = _scrollPanel.ContentBounds.Right;
            for (int row = 0; row <= _scrollPanel.MaxVisibleRows; row++)
            {
                var y = _scrollPanel.ContentBounds.Y + row * rowHeight;
                sprites.Add(new MySprite
                {
                    Type = SpriteType.TEXTURE,
                    Data = texture,
                    Position = new Vector2((contentStart + contentEnd) / 2f, y),
                    Size = new Vector2(contentEnd - contentStart, 2f),
                    Color = color,
                    Alignment = TextAlignment.CENTER
                });
            }
        }

        void DrawWrapPanelLines(List<MySprite> sprites, WrapPanelLayout layout, bool drawVerticalLines)
        {
            var lineColor = new Color(Config.HeaderColor.R, Config.HeaderColor.G, Config.HeaderColor.B);
            DrawHorizontalLines(sprites, lineColor, "SquareSimple", layout.RowHeight);

            if (!drawVerticalLines)
                return;

            var contentStart = _scrollPanel.ContentBounds.X;
            var contentEnd = _scrollPanel.ContentBounds.Right;
            var gridHeight = _scrollPanel.ContentBounds.Height;
            for (int col = 0; col <= layout.Columns; col++)
            {
                var x = col == layout.Columns ? contentEnd : contentStart + col * layout.ColumnWidth;
                sprites.Add(new MySprite
                {
                    Type = SpriteType.TEXTURE,
                    Data = "SquareSimple",
                    Position = new Vector2(x, _scrollPanel.ContentViewportBounds.Y + gridHeight / 2f),
                    Size = new Vector2(2f, gridHeight),
                    Color = lineColor,
                    Alignment = TextAlignment.CENTER
                });
            }
        }

        static void RenderPanelChildren(IReadOnlyList<ControlBase> children, ControlRenderContext context, List<MySprite> sprites)
        {
            for (int i = 0; i < children.Count; i++)
            {
                var child = children[i];
                if (child != null)
                    child.Render(context, sprites);
            }
        }

        void RemoveStalePanelChildren(Panel panel, HashSet<long> desiredIds)
        {
            var children = panel.Children;
            if (children == null)
                return;

            for (int i = children.Count - 1; i >= 0; i--)
            {
                var child = children[i];
                var entry = child == null ? null : child.DataContext as AntennaEntry;
                if (entry == null || desiredIds.Contains(entry.EntryId))
                    continue;

                panel.RemoveChild(child);
            }
        }

        static void EnsurePanelChildOrder(Panel panel, List<ControlBase> desired)
        {
            if (panel == null || desired == null)
                return;

            var children = panel.Children;
            bool changed = false;
            for (int i = 0; i < desired.Count; i++)
            {
                var child = desired[i];
                if (child == null)
                    continue;

                if (!ReferenceEquals(child.Parent, panel))
                {
                    panel.AddChild(child);
                    children = panel.Children;
                    changed = true;
                }

                if (children == null || i >= children.Count || ReferenceEquals(children[i], child))
                    continue;

                int currentIndex = IndexOfChild(children, child);
                if (currentIndex < 0)
                    continue;

                if (panel.MoveChild(child, i))
                    changed = true;
            }

            if (changed)
                panel.InvalidateLayout();
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

        void RemoveInactiveEntryModels()
        {
            _entriesToRemove.Clear();
            foreach (var kv in _entryModels)
            {
                if (!_activeEntryIds.Contains(kv.Key))
                    _entriesToRemove.Add(kv.Key);
            }

            for (int i = 0; i < _entriesToRemove.Count; i++)
            {
                var entryId = _entriesToRemove[i];
                _entryModels.Remove(entryId);

                RectangleControl control;
                if (_entryControls.TryGetValue(entryId, out control) && control != null)
                    control.SetVisible(false);
                _entryControls.Remove(entryId);
            }
        }

        public bool HasVisibleItems()
        {
            return HasEntries;
        }

        public void OnMouseScroll(int delta, ref bool handled)
        {
        }

        void OnScrollPanelChanged(ScrollPanel panel)
        {
            _interactiveHost.RenderSprites();
        }

        int GetMaxColsFromSurface()
        {
            var max = Host.ViewBox.Width - Host.ViewBox.X;
            var perCol = MINIMUM_COL_WIDTH * Host.Scale;
            return (int)Math.Max(1, Math.Round(max / perCol - .5, MidpointRounding.AwayFromZero));
        }

        float GetContentTop()
        {
            if (!Host.TitleVisible)
                return Host.ViewBox.Y;

            float layoutScale = Host.Scale * Host.Surface.FontSize;
            return Host.ViewBox.Y + (40f * layoutScale);
        }

        static float GetFooterHeight() => 0f;

        void DrawAntennaCell(List<MySprite> sprites, AntennaEntry entry, float xStart, float xEnd, float yStart, float rowHeight, bool drawAsLines)
        {
            var cellPadding = LINE * Host.Scale / 2f;
            var innerLeft = xStart + cellPadding;
            var innerRight = xEnd - cellPadding;
            var innerTop = yStart + cellPadding;
            var innerBottom = yStart + rowHeight - cellPadding;
            var topRowHeight = LINE * Host.Scale;
            var bottomRowTop = innerTop + topRowHeight;
            var bottomRowHeight = Math.Max(0f, innerBottom - bottomRowTop);
            var iconSize = innerBottom - innerTop;
            var contentLeft = innerLeft + iconSize;
            var contentWidth = Math.Max(0f, innerRight - contentLeft);

            var iconRect = new RectangleF(innerLeft, innerTop, iconSize, iconSize);
            var numberRect = new RectangleF(contentLeft, innerTop, contentWidth, topRowHeight);
            var nameRect = new RectangleF(contentLeft, bottomRowTop, contentWidth, bottomRowHeight);

            if (!drawAsLines)
            {
                var backgroundColor = !entry.IsFunctional ? Config.ErrorColor : Config.HeaderColor;
                var hsv = ColorExtensions.ColorToHSV(backgroundColor);
                hsv.Z *= 0.2f;

                var cellRect = new RectangleF(
                    xStart + cellPadding / 2f,
                    yStart + cellPadding / 2f,
                    (xEnd - xStart) - cellPadding,
                    rowHeight - cellPadding);
                var dropShadow = new RectangleF(cellRect.Position + 2, cellRect.Size);
                Border.CreateSpritesFromRect(dropShadow, sprites, hsv.HSVtoColor(), radiusScale: Host.Scale);
                Border.CreateSpritesFromRect(cellRect, sprites, backgroundColor,radiusScale: Host.Scale);
            }

            var foreground = drawAsLines ? entry.StatusColor : Host.Surface.ScriptForegroundColor;
            var iconSizeVector = new Vector2(iconRect.Width, iconRect.Height);
            var centeringOffsetY = 0f;
            var skipLaserOffset = string.Equals(entry.StatusIcon, "RotationPlane", StringComparison.Ordinal)
                                  || string.Equals(entry.StatusIcon, "GridPower", StringComparison.Ordinal)
                                  || string.Equals(entry.StatusIcon, "Search", StringComparison.Ordinal);

            if (entry.UseLaserIconCompensation && !skipLaserOffset)
            {
                var sourceCenterOffset = (LASER_ICON_TOP_PADDING - LASER_ICON_BOTTOM_PADDING) * 0.5f;
                var normalizedCenterOffset = sourceCenterOffset / LASER_ICON_SOURCE_SIZE;
                centeringOffsetY = -(iconSizeVector.Y * normalizedCenterOffset);
            }

            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXTURE,
                Data = entry.StatusIcon,
                Position = new Vector2(iconRect.X, iconRect.Y + iconRect.Height / 2f + centeringOffsetY),
                Size = iconSizeVector,
                Alignment = TextAlignment.LEFT,
                Color = entry.StatusColor
            });

            var titleSb = new StringBuilder(entry.Name ?? string.Empty);
            TrimText(titleSb, Math.Max(0f, numberRect.Width - (4f * Host.Scale)), 1.1f);
            var titlePos = numberRect.Center;
            titlePos.X = numberRect.Right;
            titlePos.Y -= numberRect.Height * 0.5f;
            sprites.Add(new MySprite(SpriteType.TEXT, titleSb.ToString(), titlePos, null, foreground, "White",
                TextAlignment.RIGHT, 1.1f * Host.Scale * Host.Surface.FontSize));

            var info = new StringBuilder();
            var lines = (entry.StatusText ?? string.Empty).Split('\n');
            var infoTrimWidth = Math.Max(0f, nameRect.Width - (6f * Host.Scale));
            for (int i = 0; i < lines.Length; i++)
            {
                var lineSb = new StringBuilder(lines[i].TrimEnd('\r'));
                TrimText(lineSb, infoTrimWidth, 0.9f);
                info.AppendLine(lineSb.ToString());
            }

            var infoPos = nameRect.Center;
            infoPos.X = nameRect.Right;
            infoPos.Y -= nameRect.Height * 0.4f;
            sprites.Add(new MySprite(SpriteType.TEXT, info.ToString(), infoPos, null, foreground, "White",
                TextAlignment.RIGHT, .9f * Host.Scale * Host.Surface.FontSize));
        }

        void RenderAntennaEntryControl(ControlBase control, ControlRenderContext context, List<MySprite> sprites)
        {
            var entry = control?.DataContext as AntennaEntry;
            if (entry == null)
                return;

            var bounds = control.Bounds;
            DrawAntennaCell(
                sprites,
                entry,
                bounds.X,
                bounds.Right,
                bounds.Y,
                bounds.Height,
                entry.DrawAsLines);
        }

        void TrimText(StringBuilder sb, float availableWidth, float fontSize = 1f)
        {
            var textSize = Host.Surface.MeasureStringInPixels(sb, "White", fontSize * Host.Scale * Host.Surface.FontSize);
            if (textSize.X <= availableWidth)
                return;

            var source = sb.ToString();
            for (int i = source.Length - 1; i > 0; i--)
            {
                sb.Clear();
                sb.Append(FormatingHelper.TrimName(source, i));
                textSize = Host.Surface.MeasureStringInPixels(sb, "White", fontSize * Host.Scale * Host.Surface.FontSize);
                if (textSize.X <= availableWidth)
                    break;
            }
        }
    }
}
