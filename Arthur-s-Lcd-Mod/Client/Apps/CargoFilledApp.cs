using System;
using System.Collections.Generic;
using System.Text;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.SurfaceScripts.Abstract;
using LcdMod.Client.Extensions;
using LcdMod.Client.Grid;
using LcdMod.Client.Gui;
using LcdMod.Client.Gui.ControlsTemplates;
using LcdMod.Client.Gui.ControlsTemplates.Panels;
using LcdMod.Client.Gui.ControlsTemplates.Progress;
using LcdMod.Client.Helpers;
using LcdMod.Client.Terminal.Controls;
using LcdMod.Common.Config.Models.Apps;
using LcdMod.Common.Helpers;
using Sandbox.ModAPI;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI;
using VRageMath;
using IMyCubeGrid = VRage.Game.ModAPI.IMyCubeGrid;
using IMySlimBlock = VRage.Game.ModAPI.IMySlimBlock;

namespace LcdMod.Client.Apps
{
    public sealed class CargoFilledApp : AppBase, IAppInteractive
    {
        const int SCROLLER_WIDTH = 8;
        const int LINE_HEIGHT = 40;
        const int SCROLL_DELAY = 12;

        readonly List<Entry> _entries = new List<Entry>();
        readonly Dictionary<long, Entry> _entryModels = new Dictionary<long, Entry>();
        readonly HashSet<long> _activeEntryIds = new HashSet<long>();
        readonly List<long> _entriesToRemove = new List<long>();
        readonly Dictionary<long, RectangleControl> _entryControls = new Dictionary<long, RectangleControl>();
        readonly List<ControlBase> _interactiveList = new List<ControlBase>();
        readonly ScrollPanel _scrollPanel;
        readonly InteractiveSurfaceScript _interactiveHost;
        ScreenConfigWithBlocks Config => (ScreenConfigWithBlocks)AppConfig;
        public bool HasEntries => _entries.Count > 0;
        public List<ControlBase> InteractiveList => _interactiveList;

        public CargoFilledApp(ScreenConfigWithBlocks config, IAppHost host) : base(config, host)
        {
            _interactiveHost = host as InteractiveSurfaceScript;
            if (_interactiveHost == null)
                throw new ArgumentException("CargoFilledApp requires an InteractiveSurfaceScript host.", "host");

            _scrollPanel = new ScrollPanel(CursorType.Default, this);
            _scrollPanel.ScrollChanged = OnScrollPanelChanged;
            _scrollPanel.SetVisible(false);
        }

        public override void Update()
        {
            _entries.Clear();
            _activeEntryIds.Clear();
            AggregateAllContainersFromGridLogic(Host.GridLogic, _entries);
            RemoveInactiveEntryModels();
            _entries.Sort((a, b) =>
            {
                var fa = a.Cap > 0 ? a.Used / a.Cap : 0;
                var fb = b.Cap > 0 ? b.Used / b.Cap : 0;
                var cmp = fb.CompareTo(fa);
                if (cmp != 0) return cmp;
                return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            });
        }

        public override List<MySprite> GetSprites()
        {
            ClearInteractiveTree();
            var sprites = new List<MySprite>();
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
            var rowHeight = LINE_HEIGHT * Host.Scale;
            var contentTop = GetContentTop();
            ConfigureScrollPanel(contentTop, rowHeight, _entries.Count);

            BeginScrollPanelClip(sprites);
            var renderContext = CreateRenderContext();

            int start = _scrollPanel.GetStartIndex(1);
            int renderRows = _scrollPanel.VisibleRows + (_scrollPanel.IsScrollable ? 1 : 0);
            int showCount = Math.Min(renderRows, _entries.Count - start);
            for (int i = 0; i < showCount; i++)
            {
                var bounds = GetListRowBounds(i, _scrollPanel.ContentBounds.Y, _scrollPanel.IsScrollable);
                var control = AddInteractiveChild(bounds, _entries[start + i], false);
                control?.Render(renderContext, sprites);
            }

            EndScrollPanelClip(sprites);
            _scrollPanel.Render(renderContext, sprites);
        }

        void DrawGrid(List<MySprite> sprites)
        {
            var rowHeight = 2f * LINE_HEIGHT * Host.Scale;
            var contentTop = GetContentTop();
            int maxCols = Math.Max(1, (int)Math.Round((Host.ViewBox.Width - Host.ViewBox.X) / (220f * Host.Scale) - .5, MidpointRounding.AwayFromZero));
            int totalRows = (int)Math.Ceiling(_entries.Count / (float)maxCols);
            ConfigureScrollPanel(contentTop, rowHeight, totalRows);

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

            if (Config.DrawLines)
            {
                var lineColor = Config.HeaderColor;
                for (int row = 0; row <= maxRows; row++)
                {
                    var y = _scrollPanel.ContentBounds.Y + row * rowHeight;
                    sprites.Add(new MySprite { Type = SpriteType.TEXTURE, Data = "SquareSimple", Position = new Vector2((contentStart + contentEnd) / 2f, y), Size = new Vector2(contentEnd - contentStart, 2f), Color = lineColor, Alignment = TextAlignment.CENTER });
                }

                for (int col = 0; col <= maxCols; col++)
                {
                    var x = contentStart + col * columnWidth;
                    sprites.Add(new MySprite { Type = SpriteType.TEXTURE, Data = "SquareSimple", Position = new Vector2(x, _scrollPanel.ContentViewportBounds.Y + gridHeight / 2f), Size = new Vector2(2f, gridHeight), Color = lineColor, Alignment = TextAlignment.CENTER });
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
                    true);
                control?.Render(renderContext, sprites);
            }

            EndScrollPanelClip(sprites);
            _scrollPanel.Render(renderContext, sprites);
        }

        void DrawRow(List<MySprite> frame, Entry entry, RectangleF bounds)
        {
            var pct = MathHelper.Clamp(entry.Cap <= 0 ? 0f : (float)(entry.Used / entry.Cap), 0f, 1f);
            Vector2 position = bounds.Position;

            if (Config.DrawLines)
                frame.Add(new MySprite { Type = SpriteType.TEXTURE, Data = "SquareSimple", Position = new Vector2(Host.ViewBox.Center.X, position.Y), Size = new Vector2(Host.ViewBox.Width, 2f), Color = Host.ForegroundColor, Alignment = TextAlignment.CENTER });

            var clip = new Rectangle((int)position.X, (int)position.Y, (int)Math.Max(0f, bounds.Width - 145 * Host.Scale), (int)bounds.Height);
            var barMargin = 8 * Host.Scale;
            Vector2 size = new Vector2(bounds.Width, clip.Height) - barMargin;

            BarPanel.CreateSprites(frame, new Vector2(clip.Location.X, clip.Location.Y + Host.Scale) + barMargin / 2f, size, Config.HeaderColor, Host.BackgroundColor.DeriveAccentColor(), pct, GetEntryUsageColor(pct));
            frame.Add(MySprite.CreateClipRect(clip));
            position.X += 16 * Host.Scale;
            position.Y += 4 * Host.Scale;
            frame.Add(new MySprite { Type = SpriteType.TEXT, Data = entry.Name, Position = position, RotationOrScale = Host.Scale, Color = Host.Surface.ScriptForegroundColor, Alignment = TextAlignment.LEFT, FontId = "White" });
            frame.Add(MySprite.CreateClearClipRect());
            position.X = bounds.Right;
            frame.Add(new MySprite { Type = SpriteType.TEXT, Data = FormatingHelper.PercentageToString(pct), Position = position, RotationOrScale = Host.Scale, Color = Host.Surface.ScriptForegroundColor, Alignment = TextAlignment.RIGHT, FontId = "White" });
        }

        void DrawGridCell(List<MySprite> frame, Entry entry, float xStart, float xEnd, float yStart, float rowHeight)
        {
            var cellPadding = (LINE_HEIGHT * Host.Scale) / 3f;
            var pct = MathHelper.Clamp(entry.Cap <= 0 ? 0f : (float)(entry.Used / entry.Cap), 0f, 1f);
            var cellView = GetCellViewBox(xStart, xEnd, yStart, rowHeight, cellPadding);

            if (!Config.DrawLines)
            {
                var backgroundColor = Config.HeaderColor;
                var hsv = VRageMath.ColorExtensions.ColorToHSV(backgroundColor);
                hsv.Z *= 0.2f;
                var cellRect = new RectangleF(xStart + cellPadding / 2f, yStart + cellPadding / 2f, (xEnd - xStart) - cellPadding, rowHeight - cellPadding);
                var dropShadow = new RectangleF(cellRect.Position + 2, cellRect.Size);
                Border.CreateSpritesFromRect(dropShadow, frame, hsv.HSVtoColor(), radiusScale: Host.Scale);
                Border.CreateSpritesFromRect(cellRect, frame, backgroundColor, radiusScale: Host.Scale);
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


        void ClearInteractiveTree()
        {
            _scrollPanel.ClearChildren();
            _scrollPanel.SetVisible(false);
            _interactiveList.Clear();

            foreach (var kv in _entryControls)
                kv.Value?.SetVisible(false);
        }

        void ConfigureScrollPanel(float contentTop, float rowHeight, int totalRows)
        {
            _scrollPanel.Configure(Host.ViewBox, contentTop, 0f, rowHeight, totalRows, SCROLLER_WIDTH * Host.Scale, SCROLL_DELAY / 6f);
            _scrollPanel.SetScrollBarColors(
                new Color(Host.Surface.ScriptForegroundColor.R, Host.Surface.ScriptForegroundColor.G, Host.Surface.ScriptForegroundColor.B, 127),
                new Color(Config.HeaderColor.R, Config.HeaderColor.G, Config.HeaderColor.B, 250));
            _scrollPanel.SetVisible(true);
            if (!_interactiveList.Contains(_scrollPanel))
                _interactiveList.Add(_scrollPanel);
        }

        RectangleF GetListRowBounds(int rowIndex, float contentTop, bool showScrollBar)
        {
            var y = contentTop + rowIndex * LINE_HEIGHT * Host.Scale;
            var left = Host.ViewBox.Position.X;
            var width = Host.ViewBox.Width - left + Host.ViewBox.X;
            if (showScrollBar)
                width -= SCROLLER_WIDTH * Host.Scale;
            return new RectangleF(left, y, width, LINE_HEIGHT * Host.Scale);
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

        RectangleControl AddInteractiveChild(RectangleF bounds, Entry dataContext, bool renderAsGrid)
        {
            if (dataContext == null)
                return null;

            dataContext.RenderAsGrid = renderAsGrid;

            RectangleControl control;
            if (!_entryControls.TryGetValue(dataContext.EntryId, out control) || control == null)
            {
                control = new RectangleControl(bounds, CursorType.Default, dataContext)
                {
                    CustomRender = RenderCargoEntryControl
                };
                _entryControls[dataContext.EntryId] = control;
            }
            else
            {
                control.SetRect(bounds);
                control.SetDataContext(dataContext);
                control.CustomRender = RenderCargoEntryControl;
            }

            control.SetVisible(true);
            _scrollPanel.AddChild(control);
            return control;
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

        Color? GetEntryUsageColor(float pct)
        {
            if (pct >= .99f)
                return Config.ErrorColor;
            if (pct > .90f)
                return Config.WarningColor;
            return null;
        }

        void RenderCargoEntryControl(ControlBase control, ControlRenderContext context, List<MySprite> sprites)
        {
            var entry = control?.DataContext as Entry;
            if (entry == null)
                return;

            var bounds = control.Bounds;
            if (entry.RenderAsGrid)
                DrawGridCell(sprites, entry, bounds.X, bounds.Right, bounds.Y, bounds.Height);
            else
                DrawRow(sprites, entry, bounds);
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

        void AggregateAllContainersFromGridLogic(GridLogic gridLogic, List<Entry> details)
        {
            if (gridLogic == null)
                return;

            var containers = gridLogic.GetTerminalBlocks<IMyCargoContainer>(Config.GridLinkType);
            if (containers == null)
                return;

            for (var i = 0; i < containers.Count; i++)
            {
                var fat = containers[i];
                if (fat == null)
                    continue;

                var config = Config;
                if (config != null && config.SelectedBlocks.Length > 0 &&
                    Array.IndexOf(config.SelectedBlocks, fat.EntityId) < 0)
                    continue;

                if (!fat.HasInventory)
                    continue;

                double localUsed = 0;
                double localCap = 0;
                var invCount = 0;
                try
                {
                    invCount = fat.InventoryCount;
                }
                catch (Exception e)
                {
                    ErrorHandlerHelper.LogError(e, Host);
                }

                for (var k = 0; k < invCount; k++)
                {
                    var inv = fat.GetInventory(k);
                    if (inv == null)
                        continue;
                    try
                    {
                        localUsed += (double)inv.CurrentVolume;
                        localCap += (double)inv.MaxVolume;
                    }
                    catch (Exception e)
                    {
                        ErrorHandlerHelper.LogError(e, Host);
                    }
                }

                if (localCap <= 0)
                    continue;

                string name;
                try
                {
                    name = fat.CustomName;
                    if (string.IsNullOrEmpty(name))
                        name = fat.DisplayNameText;
                    if (string.IsNullOrEmpty(name))
                        name = fat.BlockDefinition.SubtypeName;
                    if (string.IsNullOrEmpty(name))
                        name = "Container";
                }
                catch
                {
                    name = "Container";
                }

                var entry = GetOrCreateEntry(fat.EntityId);
                entry.Update(name, localUsed, localCap);
                details.Add(entry);
            }
        }

        Entry GetOrCreateEntry(long entryId)
        {
            _activeEntryIds.Add(entryId);

            Entry entry;
            if (!_entryModels.TryGetValue(entryId, out entry) || entry == null)
            {
                entry = new Entry(entryId);
                _entryModels[entryId] = entry;
            }

            return entry;
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

        public class Entry : ControlModelBase
        {
            public Entry(long entryId)
            {
                EntryId = entryId;
            }

            public long EntryId { get; private set; }
            public double Cap { get; private set; }
            public string Name { get; private set; }
            public double Used { get; private set; }
            public bool RenderAsGrid { get; set; }

            public void Update(string name, double used, double cap)
            {
                Name = name ?? string.Empty;
                Used = used;
                Cap = cap;
            }
        }
    }
}
