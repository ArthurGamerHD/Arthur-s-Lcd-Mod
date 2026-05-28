using System;
using System.Collections.Generic;
using System.Text;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.SurfaceScripts.Abstract;
using LcdMod.Client.Gui;
using LcdMod.Client.Gui.ControlsTemplates;
using LcdMod.Client.Gui.ControlsTemplates.Panels;
using LcdMod.Client.Gui.UserControls.Antenna;
using LcdMod.Client.Helpers;
using LcdMod.Client.Terminal.Controls;
using VRage.Game.GUI.TextPanel;
using VRageMath;
using ScreenConfigWithBlocks = LcdMod.Common.Config.Models.Apps.ScreenConfigWithBlocks;

namespace LcdMod.Client.Apps
{
    public sealed class AntennaApp : AppBase, IAppInteractive
    {
        const float LINE = 22f;
        const float MINIMUM_COL_WIDTH = 400f;
        const float SCROLLER_WIDTH = 8f;
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
        readonly InteractiveSurfaceScript _interactiveHost;
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
            ConfigureScrollPanel(caretY, footerHeight, rowHeight, _entries.Count);

            int maxRows = _scrollPanel.MaxVisibleRows;
            int start = _scrollPanel.GetStartIndex(1);
            int renderRows = _scrollPanel.VisibleRows + (_scrollPanel.IsScrollable ? 1 : 0);
            int showCount = Math.Min(renderRows, _entries.Count - start);

            float margin = 0f;
            float contentStart = Host.ViewBox.X + margin;
            float contentEnd = Host.ViewBox.Width + Host.ViewBox.X - margin;
            if (_scrollPanel.IsScrollable)
                contentEnd -= SCROLLER_WIDTH * Host.Scale;

            BeginScrollPanelClip(sprites);
            var renderContext = CreateRenderContext();

            if (Config.DrawLines)
            {
                for (int row = 0; row <= maxRows; row++)
                {
                    var y = _scrollPanel.ContentBounds.Y + row * rowHeight;
                    sprites.Add(new MySprite
                    {
                        Type = SpriteType.TEXTURE,
                        Data = "Circle",
                        Position = new Vector2((contentStart + contentEnd) / 2f, y),
                        Size = new Vector2(contentEnd - contentStart, 2f),
                        Color = Host.ForegroundColor,
                        Alignment = TextAlignment.CENTER
                    });
                }
            }

            for (int gridIdx = 0; gridIdx < showCount; gridIdx++)
            {
                int idx = start + gridIdx;
                float yStart = _scrollPanel.ContentBounds.Y + gridIdx * rowHeight;
                var control = AddInteractiveChild(
                    new RectangleF(contentStart, yStart, contentEnd - contentStart, rowHeight),
                    _entries[idx],
                    true);
                control?.Render(renderContext, sprites);
            }

            EndScrollPanelClip(sprites);
            _scrollPanel.Render(renderContext, sprites);
        }

        void DrawGridLike(List<MySprite> sprites, bool forceSingleColumn, bool drawLineSprites, bool drawVerticalLines, bool drawCellsAsLines)
        {
            var rowHeight = GRID_CELL_LINES * LINE * Host.Scale;
            float caretY = GetContentTop();
            float footerHeight = GetFooterHeight();
            int maxCols = forceSingleColumn ? 1 : Math.Max(1, GetMaxColsFromSurface());
            int totalRows = (int)Math.Ceiling(_entries.Count / (float)maxCols);
            ConfigureScrollPanel(caretY, footerHeight, rowHeight, totalRows);

            int maxRows = _scrollPanel.MaxVisibleRows;
            int start = _scrollPanel.GetStartIndex(maxCols);
            int renderRows = _scrollPanel.VisibleRows + (_scrollPanel.IsScrollable ? 1 : 0);
            int showCount = Math.Min(renderRows * maxCols, _entries.Count - start);

            float contentStart = Host.ViewBox.X;
            float contentEnd = Host.ViewBox.Width + Host.ViewBox.X;
            if (_scrollPanel.IsScrollable)
                contentEnd -= SCROLLER_WIDTH * Host.Scale;
            float columnWidth = (contentEnd - contentStart) / maxCols;
            float gridHeight = maxRows * rowHeight;

            BeginScrollPanelClip(sprites);
            var renderContext = CreateRenderContext();

            if (drawLineSprites)
            {
                var lineColor = new Color(Config.HeaderColor.R, Config.HeaderColor.G, Config.HeaderColor.B);
                for (int row = 0; row <= maxRows; row++)
                {
                    var y = _scrollPanel.ContentBounds.Y + row * rowHeight;
                    sprites.Add(new MySprite
                    {
                        Type = SpriteType.TEXTURE,
                        Data = "SquareSimple",
                        Position = new Vector2((contentStart + contentEnd) / 2f, y),
                        Size = new Vector2(contentEnd - contentStart, 2f),
                        Color = lineColor,
                        Alignment = TextAlignment.CENTER
                    });
                }

                if (drawVerticalLines)
                {
                    for (int col = 0; col <= maxCols; col++)
                    {
                        var x = contentStart + col * columnWidth;
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
            }

            for (int gridIdx = 0; gridIdx < showCount; gridIdx++)
            {
                int idx = start + gridIdx;
                int col = gridIdx % maxCols;
                int row = gridIdx / maxCols;
                float xStart = contentStart + col * columnWidth;
                float xEnd = (col == maxCols - 1) ? contentEnd : xStart + columnWidth;
                float yStart = _scrollPanel.ContentBounds.Y + row * rowHeight;
                var control = AddInteractiveChild(
                    new RectangleF(xStart, yStart, xEnd - xStart, rowHeight),
                    _entries[idx],
                    drawCellsAsLines);
                control?.Render(renderContext, sprites);
            }

            EndScrollPanelClip(sprites);
            _scrollPanel.Render(renderContext, sprites);
        }


        void ClearInteractiveTree()
        {
            _scrollPanel.ClearChildren();
            _scrollPanel.SetVisible(false);
            _interactiveList.Clear();

            foreach (var kv in _entryControls)
                kv.Value?.SetVisible(false);
        }

        void ConfigureScrollPanel(float contentTop, float footerHeight, float rowHeight, int totalRows)
        {
            _scrollPanel.Configure(Host.ViewBox, contentTop, footerHeight, rowHeight, totalRows, SCROLLER_WIDTH * Host.Scale, SCROLL_DELAY / 6f);
            _scrollPanel.SetScrollBarColors(
                new Color(Host.Surface.ScriptForegroundColor.R, Host.Surface.ScriptForegroundColor.G, Host.Surface.ScriptForegroundColor.B, 127),
                new Color(Config.HeaderColor.R, Config.HeaderColor.G, Config.HeaderColor.B, 250));
            _scrollPanel.SetVisible(true);
            if (!_interactiveList.Contains(_scrollPanel))
                _interactiveList.Add(_scrollPanel);
        }

        void BeginScrollPanelClip(List<MySprite> sprites)
        {
            if (sprites == null)
                return;

            var bounds = _scrollPanel.ContentViewportBounds;
            if (bounds.Width <= 0f || bounds.Height <= 0f)
                return;

            int x = (int)Math.Floor(bounds.X);
            int y = (int)Math.Floor(bounds.Y);
            int right = (int)Math.Ceiling(bounds.Right);
            int bottom = (int)Math.Ceiling(bounds.Bottom);
            sprites.Add(MySprite.CreateClipRect(new Rectangle(x, y, Math.Max(0, right - x), Math.Max(0, bottom - y))));
        }

        static void EndScrollPanelClip(List<MySprite> sprites)
        {
            if (sprites != null)
                sprites.Add(MySprite.CreateClearClipRect());
        }

        ControlRenderContext CreateRenderContext()
        {
            return CreateControlRenderContext(
                Host.Surface,
                Host.Scale,
                Host.Surface.FontSize,
                new Vector2(float.NaN, float.NaN));
        }

        RectangleControl AddInteractiveChild(RectangleF bounds, AntennaEntry dataContext, bool drawAsLines)
        {
            if (dataContext == null)
                return null;

            dataContext.DrawAsLines = drawAsLines;

            RectangleControl control;
            if (!_entryControls.TryGetValue(dataContext.EntryId, out control) || control == null)
            {
                control = new RectangleControl(bounds, CursorType.Default, dataContext)
                {
                    CustomRender = RenderAntennaEntryControl
                };
                _entryControls[dataContext.EntryId] = control;
            }
            else
            {
                control.SetRect(bounds);
                control.SetDataContext(dataContext);
                control.CustomRender = RenderAntennaEntryControl;
            }

            control.SetVisible(true);
            _scrollPanel.AddChild(control);
            return control;
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
