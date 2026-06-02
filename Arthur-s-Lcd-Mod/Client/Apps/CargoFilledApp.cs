using System;
using System.Collections.Generic;
using System.Text;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.Config;
using LcdMod.Client.SurfaceScripts.Abstract;
using LcdMod.Client.Extensions;
using LcdMod.Client.Grid;
using LcdMod.Client.Gui;
using LcdMod.Client.Gui.ControlsTemplates;
using LcdMod.Client.Gui.ControlsTemplates.Basic;
using LcdMod.Client.Gui.ControlsTemplates.Interactive;
using LcdMod.Client.Gui.ControlsTemplates.Panels;
using LcdMod.Client.Gui.ControlsTemplates.Progress;
using LcdMod.Client.Helpers;
using LcdMod.Client.Terminal.Controls;
using LcdMod.Common.Config.Models.Apps;
using LcdMod.Common.Helpers;
using LcdMod.Common.Networking;
using Sandbox.ModAPI;
using VRage;
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
        const string LOC_SORTER = "LcdMod_Cargo_Sorter";
        const string LOC_SORT_QUANTITY = "LcdMod_Cargo_Sort_Quantity";
        const string LOC_SORT_WEIGHT = "LcdMod_Cargo_Sort_Weight";
        const string LOC_SORT_ALPHABETICAL = "LcdMod_Cargo_Sort_Alphabetical";
        const string LOC_SORT_DONE = "LcdMod_Cargo_SortDone";
        const string LOC_SORT_REQUESTED = "LcdMod_Cargo_SortRequested";
        const string LOC_FILL_WEAPONS = "LcdMod_Cargo_FillWeapons";
        const string LOC_FILL_REACTORS = "LcdMod_Cargo_FillReactors";
        const string LOC_FILL_DONE = "LcdMod_Cargo_FillDone";
        const float FOOTER_BUTTON_GAP = 8f;
        const int STATUS_MESSAGE_FRAMES = 240; // ~4s on-screen confirmation

        readonly List<Entry> _entries = new List<Entry>();
        readonly Dictionary<long, Entry> _entryModels = new Dictionary<long, Entry>();
        readonly HashSet<long> _activeEntryIds = new HashSet<long>();
        readonly List<long> _entriesToRemove = new List<long>();
        readonly Dictionary<long, RectangleControl> _entryControls = new Dictionary<long, RectangleControl>();
        readonly List<ControlBase> _interactiveList = new List<ControlBase>();
        readonly ScrollPanel _scrollPanel;
        readonly InteractiveSurfaceScript _interactiveHost;
        readonly List<IMyTerminalBlock> _sortBlocks = new List<IMyTerminalBlock>();
        readonly List<IMyTerminalBlock> _fillTargets = new List<IMyTerminalBlock>();
        Button _sorterButton;
        Button _modeButton;
        Button _weaponsButton;
        Button _reactorsButton;
        ControlStyle _sorterButtonStyle;
        ControlStyle _modeButtonStyle;
        ControlStyle _fillButtonStyle;
        int _sortMode = (int)InventorySortMode.Quantity;
        float _footerHeight;
        long _lastSortFrame = long.MinValue;
        string _statusMessage;
        long _statusUntilFrame;
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
            _footerHeight = GetSorterButtonSize().Y + 12f * Host.Scale;
            switch (Config.DisplayMode)
            {
                case (int)DisplayMode.Grid:
                    DrawGrid(sprites);
                    break;
                default:
                    DrawList(sprites);
                    break;
            }

            DrawFooter(sprites);
            DrawStatusMessage(sprites);
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
            _scrollPanel.Configure(Host.ViewBox, contentTop, _footerHeight, rowHeight, totalRows, SCROLLER_WIDTH * Host.Scale, SCROLL_DELAY / 6f);
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
                control = new RectangleControl(bounds, CursorType.Hand, dataContext, OnEntryClicked)
                {
                    CustomRender = RenderCargoEntryControl
                };
                _entryControls[dataContext.EntryId] = control;
            }
            else
            {
                control.SetRect(bounds);
                control.SetDataContext(dataContext);
                control.SetCursor(CursorType.Hand);
                control.SetOnClick(OnEntryClicked);
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

        void DrawFooter(List<MySprite> sprites)
        {
            var modeSize = GetModeButtonSize();
            var sorterSize = GetSorterButtonSize();
            var gap = FOOTER_BUTTON_GAP * Host.Scale;
            var paddingY = 6f * Host.Scale;
            var footerTop = Host.ViewBox.Bottom - _footerHeight;

            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXTURE,
                Data = "SquareSimple",
                Position = new Vector2(Host.ViewBox.Center.X, footerTop + _footerHeight * 0.5f),
                Size = new Vector2(Host.ViewBox.Width, _footerHeight),
                Color = new Color(Host.BackgroundColor.MulValue(0.8f), 0.5f),
                Alignment = TextAlignment.CENTER
            });

            var weaponsSize = GetFillButtonSize(LOC_FILL_WEAPONS);
            var reactorsSize = GetFillButtonSize(LOC_FILL_REACTORS);

            var totalWidth = modeSize.X + sorterSize.X + weaponsSize.X + reactorsSize.X + 3f * gap;
            var x = Host.ViewBox.X + (Host.ViewBox.Width - totalWidth) * 0.5f;
            var top = footerTop + paddingY;

            var context = CreateRenderContext();
            DrawModeButton(new RectangleF(x, top, modeSize.X, modeSize.Y), context, sprites);
            x += modeSize.X + gap;
            DrawSorterButton(new RectangleF(x, top, sorterSize.X, sorterSize.Y), context, sprites);
            x += sorterSize.X + gap;
            DrawWeaponsButton(new RectangleF(x, top, weaponsSize.X, weaponsSize.Y), context, sprites);
            x += weaponsSize.X + gap;
            DrawReactorsButton(new RectangleF(x, top, reactorsSize.X, reactorsSize.Y), context, sprites);
        }

        void DrawModeButton(RectangleF rect, ControlRenderContext context, List<MySprite> sprites)
        {
            if (_modeButton == null)
                _modeButton = new Button(rect, new ButtonModel { Text = GetSortModeText(), Clicked = OnModeClicked });
            else
                _modeButton.SetRect(rect);

            var model = _modeButton.DataContext as ButtonModel;
            if (model != null)
            {
                model.Enabled = true;
                model.Text = GetSortModeText();
            }

            _modeButton.SetVisible(true);
            _modeButton.SetCursor(CursorType.Hand);
            _modeButton.SetStyle(GetModeButtonStyle());
            _modeButton.CustomRender = RenderModeButton;

            if (!_interactiveList.Contains(_modeButton))
                _interactiveList.Add(_modeButton);

            _modeButton.Render(context, sprites);
        }

        void DrawSorterButton(RectangleF rect, ControlRenderContext context, List<MySprite> sprites)
        {
            if (_sorterButton == null)
                _sorterButton = new Button(rect, new ButtonModel { Text = MyTexts.GetString(LOC_SORTER), Clicked = OnSorterClicked });
            else
                _sorterButton.SetRect(rect);

            var model = _sorterButton.DataContext as ButtonModel;
            if (model != null)
                model.Enabled = true;

            _sorterButton.SetVisible(true);
            _sorterButton.SetCursor(CursorType.Hand);
            _sorterButton.SetStyle(GetSorterButtonStyle());
            _sorterButton.CustomRender = RenderSorterButton;

            if (!_interactiveList.Contains(_sorterButton))
                _interactiveList.Add(_sorterButton);

            _sorterButton.Render(context, sprites);
        }

        void DrawWeaponsButton(RectangleF rect, ControlRenderContext context, List<MySprite> sprites)
        {
            if (_weaponsButton == null)
                _weaponsButton = new Button(rect, new ButtonModel { Text = MyTexts.GetString(LOC_FILL_WEAPONS), Clicked = OnWeaponsClicked });
            else
                _weaponsButton.SetRect(rect);

            var model = _weaponsButton.DataContext as ButtonModel;
            if (model != null)
                model.Enabled = true;

            _weaponsButton.SetVisible(true);
            _weaponsButton.SetCursor(CursorType.Hand);
            _weaponsButton.SetStyle(GetFillButtonStyle());
            _weaponsButton.CustomRender = RenderWeaponsButton;

            if (!_interactiveList.Contains(_weaponsButton))
                _interactiveList.Add(_weaponsButton);

            _weaponsButton.Render(context, sprites);
        }

        void DrawReactorsButton(RectangleF rect, ControlRenderContext context, List<MySprite> sprites)
        {
            if (_reactorsButton == null)
                _reactorsButton = new Button(rect, new ButtonModel { Text = MyTexts.GetString(LOC_FILL_REACTORS), Clicked = OnReactorsClicked });
            else
                _reactorsButton.SetRect(rect);

            var model = _reactorsButton.DataContext as ButtonModel;
            if (model != null)
                model.Enabled = true;

            _reactorsButton.SetVisible(true);
            _reactorsButton.SetCursor(CursorType.Hand);
            _reactorsButton.SetStyle(GetFillButtonStyle());
            _reactorsButton.CustomRender = RenderReactorsButton;

            if (!_interactiveList.Contains(_reactorsButton))
                _interactiveList.Add(_reactorsButton);

            _reactorsButton.Render(context, sprites);
        }

        void RenderWeaponsButton(ControlBase control, ControlRenderContext context, List<MySprite> sprites)
        {
            RenderFooterButton(control, context, sprites, MyTexts.GetString(LOC_FILL_WEAPONS));
        }

        void RenderReactorsButton(ControlBase control, ControlRenderContext context, List<MySprite> sprites)
        {
            RenderFooterButton(control, context, sprites, MyTexts.GetString(LOC_FILL_REACTORS));
        }

        ControlStyle GetFillButtonStyle()
        {
            if (_fillButtonStyle == null)
            {
                _fillButtonStyle = ControlStyle.FromThemeRoles(
                    Constants.ON_SECONDARY_CONTAINER,
                    Constants.SECONDARY_CONTAINER,
                    Constants.SECONDARY_CONTAINER + Constants.HOVER,
                    Constants.ON_SECONDARY_CONTAINER,
                    Theme);
                _fillButtonStyle.BorderRadiusPixels = Border.DEFAULT_RADIUS_PIXELS;
            }
            else
            {
                _fillButtonStyle.ThemeColors = Theme;
            }

            return _fillButtonStyle;
        }

        Vector2 GetFillButtonSize(string locKey)
        {
            var textScale = GetSorterButtonTextScale();
            var textSize = FormatingHelper.GetSizeInPixel(MyTexts.GetString(locKey), "White", textScale, Host.Surface);
            return new Vector2(
                Math.Max(110f * Host.Scale, textSize.X + 28f * Host.Scale),
                Math.Max(28f * Host.Scale, FormatingHelper.LineHeight(textScale, Host.Surface) + 10f * Host.Scale));
        }

        void OnWeaponsClicked(ButtonModel model, object sender)
        {
            RunFill(FillKind.Weapons);
        }

        void OnReactorsClicked(ButtonModel model, object sender)
        {
            RunFill(FillKind.Reactors);
        }

        // Sources are the in-scope cargo containers/connectors; targets are every weapon (ammo) or
        // reactor (uranium) reachable on the grid. Server-authoritative, like the sorter.
        void RunFill(FillKind kind)
        {
            try
            {
                // Throttle: the fill pass is a heavy synchronous transfer; ignore rapid repeat clicks.
                var frame = MyAPIGateway.Session != null ? MyAPIGateway.Session.GameplayFrameCounter : 0L;
                if (_lastSortFrame != long.MinValue && frame - _lastSortFrame < 60L)
                    return;
                _lastSortFrame = frame;

                _sortBlocks.Clear();
                CollectContainerCandidates(_sortBlocks);
                if (_sortBlocks.Count == 0)
                    return;

                _fillTargets.Clear();
                if (kind == FillKind.Weapons)
                    CollectFillTargets<IMyUserControllableGun>(_fillTargets);
                else
                    CollectFillTargets<IMyReactor>(_fillTargets);

                if (_fillTargets.Count == 0)
                    return;

                if (MyAPIGateway.Session != null && MyAPIGateway.Session.IsServer)
                {
                    var moved = BlockFillerCommon.Execute(_sortBlocks, _fillTargets, kind);
                    SetStatusMessage(string.Format(MyTexts.GetString(LOC_FILL_DONE), moved));
                }
                else
                {
                    LcdModSessionComponent.NetworkManager.TransmitToServer(
                        new PacketFillBlocks(ToEntityIds(_sortBlocks), ToEntityIds(_fillTargets), (int)kind), false);
                    SetStatusMessage(MyTexts.GetString(LOC_SORT_REQUESTED));
                }
            }
            catch (Exception e)
            {
                ErrorHandlerHelper.LogError(e, Host);
            }
        }

        void CollectFillTargets<T>(List<IMyTerminalBlock> result) where T : class
        {
            var gridLogic = Host.GridLogic;
            if (gridLogic == null)
                return;

            var blocks = gridLogic.GetTerminalBlocks<IMyTerminalBlock>(Config.GridLinkType);
            if (blocks == null)
                return;

            for (var i = 0; i < blocks.Count; i++)
            {
                var fat = blocks[i];
                if (fat == null || !fat.HasInventory)
                    continue;

                if (fat is T)
                    result.Add(fat);
            }
        }

        static long[] ToEntityIds(List<IMyTerminalBlock> blocks)
        {
            var ids = new long[blocks.Count];
            for (var i = 0; i < blocks.Count; i++)
                ids[i] = blocks[i].EntityId;
            return ids;
        }

        string GetSortModeText()
        {
            switch ((InventorySortMode)_sortMode)
            {
                case InventorySortMode.Weight:
                    return MyTexts.GetString(LOC_SORT_WEIGHT);
                case InventorySortMode.Alphabetical:
                    return MyTexts.GetString(LOC_SORT_ALPHABETICAL);
                default:
                    return MyTexts.GetString(LOC_SORT_QUANTITY);
            }
        }

        float GetSorterButtonTextScale()
        {
            return 0.6f * Host.Scale * Host.Surface.FontSize;
        }

        Vector2 GetSorterButtonSize()
        {
            var textScale = GetSorterButtonTextScale();
            var textSize = FormatingHelper.GetSizeInPixel(MyTexts.GetString(LOC_SORTER), "White", textScale, Host.Surface);
            return new Vector2(
                Math.Max(120f * Host.Scale, textSize.X + 28f * Host.Scale),
                Math.Max(28f * Host.Scale, FormatingHelper.LineHeight(textScale, Host.Surface) + 10f * Host.Scale));
        }

        Vector2 GetModeButtonSize()
        {
            var textScale = GetSorterButtonTextScale();
            // Size to the widest label so cycling the mode never reflows the footer.
            var width = 0f;
            width = Math.Max(width, FormatingHelper.GetSizeInPixel(MyTexts.GetString(LOC_SORT_QUANTITY), "White", textScale, Host.Surface).X);
            width = Math.Max(width, FormatingHelper.GetSizeInPixel(MyTexts.GetString(LOC_SORT_WEIGHT), "White", textScale, Host.Surface).X);
            width = Math.Max(width, FormatingHelper.GetSizeInPixel(MyTexts.GetString(LOC_SORT_ALPHABETICAL), "White", textScale, Host.Surface).X);
            return new Vector2(
                Math.Max(110f * Host.Scale, width + 28f * Host.Scale),
                Math.Max(28f * Host.Scale, FormatingHelper.LineHeight(textScale, Host.Surface) + 10f * Host.Scale));
        }

        ControlStyle GetSorterButtonStyle()
        {
            if (_sorterButtonStyle == null)
                _sorterButtonStyle = Button.CreatePrimaryButtonStyle(Theme);
            else
                _sorterButtonStyle.ThemeColors = Theme;

            return _sorterButtonStyle;
        }

        ControlStyle GetModeButtonStyle()
        {
            if (_modeButtonStyle == null)
            {
                _modeButtonStyle = ControlStyle.FromThemeRoles(
                    Constants.ON_SECONDARY_CONTAINER,
                    Constants.SECONDARY_CONTAINER,
                    Constants.SECONDARY_CONTAINER + Constants.HOVER,
                    Constants.ON_SECONDARY_CONTAINER,
                    Theme);
                _modeButtonStyle.BorderRadiusPixels = Border.DEFAULT_RADIUS_PIXELS;
            }
            else
            {
                _modeButtonStyle.ThemeColors = Theme;
            }

            return _modeButtonStyle;
        }

        void RenderSorterButton(ControlBase control, ControlRenderContext context, List<MySprite> sprites)
        {
            RenderFooterButton(control, context, sprites, MyTexts.GetString(LOC_SORTER));
        }

        void RenderModeButton(ControlBase control, ControlRenderContext context, List<MySprite> sprites)
        {
            RenderFooterButton(control, context, sprites, GetSortModeText());
        }

        void RenderFooterButton(ControlBase control, ControlRenderContext context, List<MySprite> sprites, string text)
        {
            var rect = control.Bounds;
            var hover = rect.Contains(context.CursorPosition);
            var buttonColor = context.Style.GetPanelColor(hover);
            var textColor = context.Style.GetTextColor(hover);
            var textScale = GetSorterButtonTextScale();

            Border.CreateSpritesFromRect(rect, sprites, buttonColor, radiusScale: context.Scale);
            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT,
                Data = text,
                Position = new Vector2(rect.Center.X,
                    rect.Center.Y - FormatingHelper.GetSizeInPixel(text, "White", textScale, context.Surface).Y * 0.5f),
                RotationOrScale = textScale,
                Color = textColor,
                Alignment = TextAlignment.CENTER,
                FontId = "White"
            });
        }

        void OnModeClicked(ButtonModel model, object sender)
        {
            try
            {
                _sortMode = (_sortMode + 1) % 3;
                _interactiveHost.RenderSprites();
            }
            catch (Exception e)
            {
                ErrorHandlerHelper.LogError(e, Host);
            }
        }

        void OnSorterClicked(ButtonModel model, object sender)
        {
            try
            {
                // Throttle: the consolidation is a heavy synchronous pass; ignore rapid repeat clicks.
                // Guard the sentinel explicitly: (frame - long.MinValue) overflows and would block
                // the very first click forever.
                var frame = MyAPIGateway.Session != null ? MyAPIGateway.Session.GameplayFrameCounter : 0L;
                if (_lastSortFrame != long.MinValue && frame - _lastSortFrame < 60L)
                    return;
                _lastSortFrame = frame;

                _sortBlocks.Clear();
                CollectFilteredContainers(_sortBlocks);
                if (_sortBlocks.Count < 2)
                    return;

                // Single-player / listen-server host executes directly; pure clients ask the server,
                // which is authoritative over inventory transfers in multiplayer.
                if (MyAPIGateway.Session != null && MyAPIGateway.Session.IsServer)
                {
                    var moved = InventorySorterCommon.Consolidate(_sortBlocks, (InventorySortMode)_sortMode);
                    SetStatusMessage(string.Format(MyTexts.GetString(LOC_SORT_DONE), moved));
                }
                else
                {
                    var ids = new long[_sortBlocks.Count];
                    for (var i = 0; i < _sortBlocks.Count; i++)
                        ids[i] = _sortBlocks[i].EntityId;

                    LcdModSessionComponent.NetworkManager.TransmitToServer(new PacketSortInventory(ids, _sortMode), false);
                    SetStatusMessage(MyTexts.GetString(LOC_SORT_REQUESTED));
                }
            }
            catch (Exception e)
            {
                ErrorHandlerHelper.LogError(e, Host);
            }
        }

        void SetStatusMessage(string message)
        {
            _statusMessage = message;
            var frame = MyAPIGateway.Session != null ? MyAPIGateway.Session.GameplayFrameCounter : 0L;
            _statusUntilFrame = frame + STATUS_MESSAGE_FRAMES;
            _interactiveHost.RenderSprites();
        }

        // Transient confirmation banner drawn on the LCD itself (centered), instead of a HUD popup.
        void DrawStatusMessage(List<MySprite> sprites)
        {
            if (string.IsNullOrEmpty(_statusMessage))
                return;

            var frame = MyAPIGateway.Session != null ? MyAPIGateway.Session.GameplayFrameCounter : 0L;
            if (frame >= _statusUntilFrame)
            {
                _statusMessage = null;
                return;
            }

            var textScale = 0.9f * Host.Scale * Host.Surface.FontSize;
            var textSize = FormatingHelper.GetSizeInPixel(_statusMessage, "White", textScale, Host.Surface);
            var padX = 20f * Host.Scale;
            var padY = 12f * Host.Scale;
            var rect = new RectangleF(
                Host.ViewBox.Center.X - (textSize.X * 0.5f + padX),
                Host.ViewBox.Center.Y - (textSize.Y * 0.5f + padY),
                textSize.X + 2f * padX,
                textSize.Y + 2f * padY);

            Border.CreateSpritesFromRect(rect, sprites,
                new Color(Host.BackgroundColor.MulValue(0.2f), 0.92f), radiusScale: Host.Scale);
            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT,
                Data = _statusMessage,
                Position = new Vector2(rect.Center.X, rect.Center.Y - textSize.Y * 0.5f),
                RotationOrScale = textScale,
                Color = Host.Surface.ScriptForegroundColor,
                Alignment = TextAlignment.CENTER,
                FontId = "White"
            });
        }

        void OnEntryClicked(object dataContext, object sender)
        {
            var entry = dataContext as Entry;
            if (entry == null)
                return;

            // Defer: opening the dialog re-renders and rebuilds the ScrollPanel children, which is
            // unsafe to do from inside a ScrollPanel child's click dispatch.
            var entityId = entry.EntryId;
            LcdModClientComponent.RunNextFrame.Add(delegate { OpenActionDialog(entityId); });
        }

        void OpenActionDialog(long entityId)
        {
            try
            {
                var source = MyAPIGateway.Entities != null
                    ? MyAPIGateway.Entities.GetEntityById(entityId) as IMyTerminalBlock
                    : null;
                if (source == null || !source.HasInventory)
                    return;

                var candidates = new List<IMyTerminalBlock>();
                CollectContainerCandidates(candidates);

                var dialog = new ContainerActionDialog(this, source, candidates,
                    delegate(Dialog d) { _interactiveHost.ShowDialog(d); },
                    Config.SortFilterKeys,
                    Config.SortFilterCategories,
                    SaveSortFilter);
                _interactiveHost.ShowDialog(dialog);
            }
            catch (Exception e)
            {
                ErrorHandlerHelper.LogError(e, Host);
            }
        }

        void SaveSortFilter(List<string> keys, List<string> categories)
        {
            try
            {
                Config.SortFilterKeys = keys != null ? keys.ToArray() : Array.Empty<string>();
                Config.SortFilterCategories = categories != null ? categories.ToArray() : Array.Empty<string>();
                var block = Host.Block as IMyTerminalBlock;
                if (block != null)
                    ConfigManager.Sync(block);
            }
            catch (Exception e)
            {
                ErrorHandlerHelper.LogError(e, Host);
            }
        }

        void CollectContainerCandidates(List<IMyTerminalBlock> result)
        {
            var gridLogic = Host.GridLogic;
            if (gridLogic == null)
                return;

            var blocks = gridLogic.GetTerminalBlocks<IMyTerminalBlock>(Config.GridLinkType);
            if (blocks == null)
                return;

            for (var i = 0; i < blocks.Count; i++)
            {
                var fat = blocks[i];
                if (fat == null || !fat.HasInventory)
                    continue;

                if (!(fat is IMyCargoContainer || fat is IMyShipConnector))
                    continue;

                if (Config.SelectedBlocks.Length > 0 && Array.IndexOf(Config.SelectedBlocks, fat.EntityId) < 0)
                    continue;

                result.Add(fat);
            }
        }

        void CollectFilteredContainers(List<IMyTerminalBlock> result)
        {
            var gridLogic = Host.GridLogic;
            if (gridLogic == null)
                return;

            // The Sorter also drains connectors, assemblers, refineries and ship tools (welder/
            // grinder/drill) into the cargo containers, so scan every terminal block in scope and
            // keep the inventory-bearing ones we care about. (The list itself shows only containers
            // and refineries.)
            var blocks = gridLogic.GetTerminalBlocks<IMyTerminalBlock>(Config.GridLinkType);
            if (blocks == null)
                return;

            for (var i = 0; i < blocks.Count; i++)
            {
                var fat = blocks[i];
                if (fat == null || !fat.HasInventory)
                    continue;

                if (!(fat is IMyCargoContainer || fat is IMyShipConnector || fat is IMyAssembler
                      || fat is IMyRefinery || fat is IMyShipToolBase))
                    continue;

                if (Config.SelectedBlocks.Length > 0 && Array.IndexOf(Config.SelectedBlocks, fat.EntityId) < 0)
                    continue;

                result.Add(fat);
            }
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

            var blocks = gridLogic.GetTerminalBlocks<IMyTerminalBlock>(Config.GridLinkType);
            if (blocks == null)
                return;

            for (var i = 0; i < blocks.Count; i++)
            {
                var fat = blocks[i];
                if (fat == null)
                    continue;

                // Chart cargo containers (all inventories) and refineries (input inventory only).
                var isRefinery = fat is IMyRefinery;
                if (!(fat is IMyCargoContainer || isRefinery))
                    continue;

                var config = Config;
                if (config != null && config.SelectedBlocks.Length > 0 &&
                    Array.IndexOf(config.SelectedBlocks, fat.EntityId) < 0)
                    continue;

                if (!fat.HasInventory)
                    continue;

                double localUsed = 0;
                double localCap = 0;
                if (isRefinery)
                {
                    // Refineries: only the input inventory (index 0) is charted.
                    var inv = fat.GetInventory(0);
                    if (inv != null)
                    {
                        try
                        {
                            localUsed = (double)inv.CurrentVolume;
                            localCap = (double)inv.MaxVolume;
                        }
                        catch (Exception e)
                        {
                            ErrorHandlerHelper.LogError(e, Host);
                        }
                    }
                }
                else
                {
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
