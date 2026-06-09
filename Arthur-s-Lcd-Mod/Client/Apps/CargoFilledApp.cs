using System;
using System.Collections.Generic;
using System.Text;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.Config;
using LcdMod.Client.Extensions;
using LcdMod.Client.Grid;
using LcdMod.Client.Gui;
using LcdMod.Client.Gui.ControlsTemplates;
using LcdMod.Client.Gui.ControlsTemplates.Dialogs;
using LcdMod.Client.Gui.ControlsTemplates.Interactive;
using LcdMod.Client.Gui.ControlsTemplates.Panels;
using LcdMod.Client.Gui.ControlsTemplates.Panels.WrapPanel;
using LcdMod.Client.Gui.ControlsTemplates.Progress;
using LcdMod.Client.Helpers;
using LcdMod.Client.SurfaceScripts.Abstract;
using LcdMod.Client.Terminal.Controls;
using LcdMod.Common.Config.Models.Apps;
using LcdMod.Common.Helpers;
using Sandbox.ModAPI;
using VRage.Game.GUI.TextPanel;
using VRageMath;
using VisualStackPanel = LcdMod.Client.Gui.ControlsTemplates.Panels.StackPanel.StackPanel;
using VisualWrapPanel = LcdMod.Client.Gui.ControlsTemplates.Panels.WrapPanel.WrapPanel;

namespace LcdMod.Client.Apps
{
    public sealed class CargoFilledApp : AppBase, IAppInteractive
    {
        private const int LINE_HEIGHT = 40;
        private const int SCROLL_DELAY = 12;
        private const int STATUS_MESSAGE_FRAMES = 240;
        private readonly HashSet<long> _activeEntryIds = new HashSet<long>();

        private readonly List<Entry> _entries = new List<Entry>();
        private readonly List<long> _entriesToRemove = new List<long>();
        private readonly Dictionary<long, RectangleControl> _entryControls = new Dictionary<long, RectangleControl>();
        private readonly Dictionary<long, Entry> _entryModels = new Dictionary<long, Entry>();
        private readonly InteractiveSurfaceScript _interactiveHost;
        private readonly ScrollPanel _scrollPanel;
        private readonly VisualStackPanel _listPanel;
        private readonly VisualWrapPanel _gridPanel;
        private string _statusMessage;
        private long _statusUntilFrame;

        public CargoFilledApp(ScreenConfigWithBlocks config, IAppHost host) : base(config, host)
        {
            _interactiveHost = host as InteractiveSurfaceScript;
            if (_interactiveHost == null)
                throw new ArgumentException("CargoFilledApp requires an InteractiveSurfaceScript host.", "host");

            _scrollPanel = new ScrollPanel(CursorType.Default, this);
            _scrollPanel.ScrollChanged = OnScrollPanelChanged;
            _scrollPanel.SetVisible(false);
            _listPanel = new VisualStackPanel();
            _gridPanel = new VisualWrapPanel();
            _gridPanel.CustomRender = RenderGridPanelContent;
        }

        private ScreenConfigWithBlocks Config => (ScreenConfigWithBlocks)AppConfig;
        public bool HasEntries => _entries.Count > 0;
        public List<ControlBase> InteractiveList { get; } = new List<ControlBase>();

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

            DrawStatusMessage(sprites);
            return sprites;
        }

        public bool HasVisibleItems()
        {
            return HasEntries;
        }

        public void OnMouseScroll(int delta, ref bool handled)
        {
        }

        private void DrawList(List<MySprite> sprites)
        {
            var rowHeight = LINE_HEIGHT * Host.Proportion;
            var contentTop = GetContentTop();
            _scrollPanel.SetContent(_listPanel);
            _listPanel.RowHeight = rowHeight;
            _listPanel.Gap = 0f;
            SyncPanelChildren(_listPanel, _entries, false);
            ConfigureScrollPanel(contentTop, rowHeight);

            var renderContext = CreateRenderContext();
            _scrollPanel.Render(renderContext, sprites);
        }

        private void DrawGrid(List<MySprite> sprites)
        {
            var rowHeight = 2f * LINE_HEIGHT * Host.Proportion;
            var contentTop = GetContentTop();
            _scrollPanel.SetContent(_gridPanel);
            _gridPanel.RowHeight = rowHeight;
            _gridPanel.MinimumColumnWidth = 220f * Host.Proportion;
            _gridPanel.HorizontalGap = 0f;
            _gridPanel.VerticalGap = 0f;
            SyncPanelChildren(_gridPanel, _entries, true);
            ConfigureScrollPanel(contentTop, rowHeight);

            var renderContext = CreateRenderContext();
            _scrollPanel.Render(renderContext, sprites);
        }

        private void DrawRow(List<MySprite> frame, Entry entry, RectangleF bounds)
        {
            var pct = MathHelper.Clamp(entry.Cap <= 0 ? 0f : (float)(entry.Used / entry.Cap), 0f, 1f);
            var position = bounds.Position;

            if (Config.DrawLines)
                frame.Add(new MySprite
                {
                    Type = SpriteType.TEXTURE, Data = "SquareSimple",
                    Position = new Vector2(bounds.Center.X, position.Y),
                    Size = new Vector2(bounds.Width, 2f), Color = Host.ForegroundColor,
                    Alignment = TextAlignment.CENTER
                });

            var barMargin = 8 * Host.Proportion;
            var size = new Vector2(bounds.Width, bounds.Height) - barMargin;
            var rowClip = new RectangleF(
                bounds.X,
                bounds.Y,
                Math.Max(0f, bounds.Width - 145f * Host.Proportion),
                bounds.Height);

            if (BeginNestedClip(frame, rowClip))
            {
                var activeRowClip = Intersect(rowClip, _scrollPanel.ContentViewportBounds);
                DrawClippedProgressBar(
                    frame,
                    new Vector2(position.X, position.Y + Host.Proportion) + barMargin / 2f,
                    size,
                    pct,
                    activeRowClip);
                position.X += 16 * Host.Proportion;
                position.Y += 4 * Host.Proportion;
                frame.Add(new MySprite
                {
                    Type = SpriteType.TEXT, Data = entry.Name, Position = position, RotationOrScale = Host.Proportion,
                    Color = Host.Surface.ScriptForegroundColor, Alignment = TextAlignment.LEFT, FontId = "White"
                });
                EndNestedClipAndRestoreScrollClip(frame);
            }

            position.X = bounds.Right;
            frame.Add(new MySprite
            {
                Type = SpriteType.TEXT, Data = FormatingHelper.PercentageToString(pct), Position = position,
                RotationOrScale = Host.Proportion, Color = Host.Surface.ScriptForegroundColor,
                Alignment = TextAlignment.RIGHT, FontId = "White"
            });
        }

        private void DrawClippedProgressBar(
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

        private bool BeginNestedClip(List<MySprite> sprites, RectangleF bounds)
        {
            var clip = Intersect(bounds, _scrollPanel.ContentViewportBounds);
            if (clip.Width <= 0f || clip.Height <= 0f)
                return false;

            AddClip(sprites, clip);
            return true;
        }

        private void EndNestedClipAndRestoreScrollClip(List<MySprite> sprites)
        {
            if (sprites == null)
                return;

            sprites.Add(MySprite.CreateClearClipRect());
            AddClip(sprites, _scrollPanel.ContentViewportBounds);
        }

        private static void EndNestedClipAndRestoreClip(List<MySprite> sprites, RectangleF clip)
        {
            if (sprites == null)
                return;

            sprites.Add(MySprite.CreateClearClipRect());
            AddClip(sprites, clip);
        }

        private static RectangleF Intersect(RectangleF a, RectangleF b)
        {
            var x = Math.Max(a.X, b.X);
            var y = Math.Max(a.Y, b.Y);
            var right = Math.Min(a.Right, b.Right);
            var bottom = Math.Min(a.Bottom, b.Bottom);
            return new RectangleF(x, y, Math.Max(0f, right - x), Math.Max(0f, bottom - y));
        }

        private static void AddClip(List<MySprite> sprites, RectangleF bounds)
        {
            if (sprites == null)
                return;

            var x = (int)Math.Floor(bounds.X);
            var y = (int)Math.Floor(bounds.Y);
            var right = (int)Math.Ceiling(bounds.Right);
            var bottom = (int)Math.Ceiling(bounds.Bottom);
            sprites.Add(MySprite.CreateClipRect(new Rectangle(x, y, Math.Max(0, right - x), Math.Max(0, bottom - y))));
        }

        private void DrawGridCell(List<MySprite> frame, Entry entry, float xStart, float xEnd, float yStart,
            float rowHeight)
        {
            var cellPadding = LINE_HEIGHT * Host.Proportion / 3f;
            var pct = MathHelper.Clamp(entry.Cap <= 0 ? 0f : (float)(entry.Used / entry.Cap), 0f, 1f);
            var cellView = GetCellViewBox(xStart, xEnd, yStart, rowHeight, cellPadding);

            if (!Config.DrawLines)
            {
                var backgroundColor = Config.HeaderColor;
                var hsv = backgroundColor.ColorToHSV();
                hsv.Z *= 0.2f;
                var cellRect = new RectangleF(xStart + cellPadding / 2f, yStart + cellPadding / 2f,
                    xEnd - xStart - cellPadding, rowHeight - cellPadding);
                var dropShadow = new RectangleF(cellRect.Position + 2, cellRect.Size);
                Border.CreateSpritesFromRect(dropShadow, frame, hsv.HSVtoColor(), radiusScale: Host.Proportion);
                Border.CreateSpritesFromRect(cellRect, frame, backgroundColor, radiusScale: Host.Proportion);
            }

            var nameHeight = Math.Max(0f, cellView.Height * .45f);
            var nameRect = new RectangleF(cellView.X, cellView.Y, cellView.Width, nameHeight);
            var bottomRect = new RectangleF(cellView.X, nameRect.Bottom, cellView.Width,
                Math.Max(0f, cellView.Bottom - nameRect.Bottom));
            var name = new StringBuilder(entry.Name ?? string.Empty);
            TrimText(ref name, nameRect.Width);
            frame.Add(new MySprite
            {
                Type = SpriteType.TEXT, Data = name.ToString(),
                Position = new Vector2(nameRect.X + 2f * Host.Proportion, nameRect.Y + 2f * Host.Proportion),
                RotationOrScale = .9f * Host.Proportion, Color = Host.Surface.ScriptForegroundColor,
                Alignment = TextAlignment.LEFT, FontId = "White"
            });

            var barWidth = bottomRect.Width * (2f / 3f);
            var textRect = new RectangleF(bottomRect.X + barWidth, bottomRect.Y, bottomRect.Width - barWidth,
                bottomRect.Height);
            var barRect = new RectangleF(bottomRect.X, bottomRect.Y, barWidth, bottomRect.Height);
            var barInnerPaddingX = 2f * Host.Proportion;
            var barInnerPaddingY = bottomRect.Height * 0.2f;
            var fillColor = Config.HeaderColor.DeriveAccentColor(.4f, 0.5);
            BarPanel.CreateSprites(frame,
                new Vector2(barRect.X + barInnerPaddingX, barRect.Y + barInnerPaddingY + 2f * Host.Proportion),
                new Vector2(Math.Max(1f, barRect.Width - 2f * barInnerPaddingX),
                    Math.Max(1f, barRect.Height - 2f * barInnerPaddingY)), fillColor,
                fillColor.DeriveAccentColor(.6f, 0.7), pct, GetEntryUsageColor(pct));
            frame.Add(new MySprite
            {
                Type = SpriteType.TEXT, Data = FormatingHelper.PercentageToString(pct),
                Position = new Vector2(textRect.Right - 2f * Host.Proportion, textRect.Y + 2f * Host.Proportion),
                RotationOrScale = .95f * Host.Proportion, Color = Host.Surface.ScriptForegroundColor,
                Alignment = TextAlignment.RIGHT, FontId = "White"
            });
        }


        private void ClearInteractiveTree()
        {
            _scrollPanel.SetVisible(false);
            InteractiveList.Clear();

            foreach (var kv in _entryControls)
                kv.Value?.SetVisible(false);
        }

        private void ConfigureScrollPanel(float contentTop, float rowHeight)
        {
            var viewportHeight = Math.Max(0f, Host.ViewBox.Bottom - contentTop);
            _scrollPanel.ConfigureAutomatic(
                new RectangleF(Host.ViewBox.X, contentTop, Host.ViewBox.Width, viewportHeight),
                ScrollPanel.DefaultScrollerWidthPixels * Host.Proportion,
                rowHeight,
                SCROLL_DELAY / 6f);
            _scrollPanel.SetScrollBarColors(
                new Color(Host.Surface.ScriptForegroundColor.R, Host.Surface.ScriptForegroundColor.G,
                    Host.Surface.ScriptForegroundColor.B, 127),
                new Color(Config.HeaderColor.R, Config.HeaderColor.G, Config.HeaderColor.B, 250));
            _scrollPanel.SetVisible(true);
            if (!InteractiveList.Contains(_scrollPanel))
                InteractiveList.Add(_scrollPanel);
        }

        private ControlRenderContext CreateRenderContext()
        {
            return CreateControlRenderContext(
                Host.Surface,
                Host.Proportion,
                Host.Surface.FontSize,
                new Vector2(float.NaN, float.NaN));
        }

        private RectangleControl GetOrCreateEntryControl(Entry dataContext, bool renderAsGrid)
        {
            if (dataContext == null)
                return null;

            dataContext.RenderAsGrid = renderAsGrid;

            RectangleControl control;
            if (!_entryControls.TryGetValue(dataContext.EntryId, out control) || control == null)
            {
                control = new RectangleControl(default(RectangleF), CursorType.Hand, dataContext, OnEntryClicked)
                {
                    CustomRender = RenderCargoEntryControl
                };
                _entryControls[dataContext.EntryId] = control;
            }
            else
            {
                control.SetDataContext(dataContext);
                control.SetCursor(CursorType.Hand);
                control.SetOnClick(OnEntryClicked);
                control.CustomRender = RenderCargoEntryControl;
            }

            control.SetVisible(true);
            return control;
        }

        private void SyncPanelChildren(Panel panel, List<Entry> entries, bool renderAsGrid)
        {
            if (panel == null)
                return;

            var desired = new List<ControlBase>(entries == null ? 0 : entries.Count);
            var desiredIds = new HashSet<long>();
            if (entries != null)
            {
                for (var i = 0; i < entries.Count; i++)
                {
                    var entry = entries[i];
                    if (entry == null)
                        continue;

                    desiredIds.Add(entry.EntryId);
                    desired.Add(GetOrCreateEntryControl(entry, renderAsGrid));
                }
            }

            RemoveStalePanelChildren(panel, desiredIds);
            EnsurePanelChildOrder(panel, desired);
        }

        private void RenderGridPanelContent(ControlBase control, ControlRenderContext context, List<MySprite> sprites)
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
                    children.Count);
                DrawGridLines(sprites, layout);
            }

            for (var i = 0; i < children.Count; i++)
            {
                var child = children[i];
                if (child != null)
                    child.Render(context, sprites);
            }
        }

        private void DrawGridLines(List<MySprite> sprites, WrapPanelLayout layout)
        {
            var lineColor = Config.HeaderColor;
            var contentStart = _scrollPanel.ContentBounds.X;
            var contentEnd = _scrollPanel.ContentBounds.Right;
            var gridHeight = _scrollPanel.ContentBounds.Height;

            for (var row = 0; row <= _scrollPanel.MaxVisibleRows; row++)
            {
                var y = _scrollPanel.ContentBounds.Y + row * layout.RowHeight;
                sprites.Add(new MySprite
                {
                    Type = SpriteType.TEXTURE, Data = "SquareSimple",
                    Position = new Vector2((contentStart + contentEnd) / 2f, y),
                    Size = new Vector2(contentEnd - contentStart, 2f), Color = lineColor,
                    Alignment = TextAlignment.CENTER
                });
            }

            for (var col = 0; col <= layout.Columns; col++)
            {
                var x = col == layout.Columns ? contentEnd : contentStart + col * layout.ColumnWidth;
                sprites.Add(new MySprite
                {
                    Type = SpriteType.TEXTURE, Data = "SquareSimple",
                    Position = new Vector2(x, _scrollPanel.ContentViewportBounds.Y + gridHeight / 2f),
                    Size = new Vector2(2f, gridHeight), Color = lineColor, Alignment = TextAlignment.CENTER
                });
            }
        }

        private static void RemoveStalePanelChildren(Panel panel, HashSet<long> desiredIds)
        {
            var children = panel.Children;
            if (children == null)
                return;

            for (var i = children.Count - 1; i >= 0; i--)
            {
                var child = children[i];
                var entry = child == null ? null : child.DataContext as Entry;
                if (entry == null || desiredIds.Contains(entry.EntryId))
                    continue;

                panel.RemoveChild(child);
            }
        }

        private static void EnsurePanelChildOrder(Panel panel, List<ControlBase> desired)
        {
            if (panel == null || desired == null)
                return;

            var children = panel.Children;
            var changed = false;
            for (var i = 0; i < desired.Count; i++)
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

                var currentIndex = IndexOfChild(children, child);
                if (currentIndex < 0)
                    continue;

                if (panel.MoveChild(child, i))
                    changed = true;
            }

            if (changed)
                panel.InvalidateLayout();
        }

        private static int IndexOfChild(IReadOnlyList<ControlBase> children, ControlBase child)
        {
            if (children == null || child == null)
                return -1;

            for (var i = 0; i < children.Count; i++)
                if (ReferenceEquals(children[i], child))
                    return i;

            return -1;
        }

        private void OnScrollPanelChanged(ScrollPanel panel)
        {
            _interactiveHost.RenderSprites();
        }

        private Color? GetEntryUsageColor(float pct)
        {
            if (pct >= .99f)
                return Config.ErrorColor;
            if (pct > .90f)
                return Config.WarningColor;
            return null;
        }

        private void RenderCargoEntryControl(ControlBase control, ControlRenderContext context, List<MySprite> sprites)
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

        private float GetContentTop()
        {
            return Host.TitleVisible ? Host.ViewBox.Y + 40f * Host.Proportion * Host.Surface.FontSize : Host.ViewBox.Y;
        }

        private void OnEntryClicked(object dataContext, object sender)
        {
            var entry = dataContext as Entry;
            if (entry == null)
                return;

            var entityId = entry.EntryId;
            LcdModClientComponent.RunNextFrame.Add(delegate { OpenActionDialog(entityId); });
        }

        private void OpenActionDialog(long entityId)
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
                    SaveSortFilter,
                    SetStatusMessage);
                _interactiveHost.ShowDialog(dialog);
            }
            catch (Exception e)
            {
                ErrorHandlerHelper.LogError(e, Host);
            }
        }

        private void SaveSortFilter(List<string> keys, List<string> categories)
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

        private void SetStatusMessage(string message)
        {
            _statusMessage = message;
            var frame = MyAPIGateway.Session != null ? MyAPIGateway.Session.GameplayFrameCounter : 0L;
            _statusUntilFrame = frame + STATUS_MESSAGE_FRAMES;
            _interactiveHost.RenderSprites();
        }

        private void DrawStatusMessage(List<MySprite> sprites)
        {
            if (string.IsNullOrEmpty(_statusMessage))
                return;

            var frame = MyAPIGateway.Session != null ? MyAPIGateway.Session.GameplayFrameCounter : 0L;
            if (frame >= _statusUntilFrame)
            {
                _statusMessage = null;
                return;
            }

            var textScale = 0.9f * Host.Proportion * Host.Surface.FontSize;
            var textSize = FormatingHelper.GetSizeInPixel(_statusMessage, "White", textScale, Host.Surface);
            var padX = 20f * Host.Proportion;
            var padY = 12f * Host.Proportion;
            var rect = new RectangleF(
                Host.ViewBox.Center.X - (textSize.X * 0.5f + padX),
                Host.ViewBox.Center.Y - (textSize.Y * 0.5f + padY),
                textSize.X + 2f * padX,
                textSize.Y + 2f * padY);

            Border.CreateSpritesFromRect(rect, sprites,
                Host.BackgroundColor.MulValue(0.2f), radiusScale: Host.Proportion);
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

        private void CollectContainerCandidates(List<IMyTerminalBlock> result)
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

        private RectangleF GetCellViewBox(float xStart, float xEnd, float yStart, float cellHeight, float cellPadding)
        {
            var innerLeft = xStart + cellPadding;
            var innerRight = xEnd - cellPadding;
            var innerTop = yStart + cellPadding;
            var innerBottom = yStart + cellHeight - cellPadding;
            return new RectangleF(innerLeft, innerTop, innerRight - innerLeft, innerBottom - innerTop);
        }

        private void TrimText(ref StringBuilder sb, float availableWidth, float fontSize = 1f)
        {
            var textSize = Host.Surface.MeasureStringInPixels(sb, "White", fontSize * Host.Proportion);
            if (textSize.X <= availableWidth)
                return;

            var source = sb.ToString();
            for (var i = source.Length - 1; i > 0; i--)
            {
                sb.Clear();
                sb.Append(FormatingHelper.TrimName(source, i));
                textSize = Host.Surface.MeasureStringInPixels(sb, "White", fontSize * Host.Proportion);
                if (textSize.X <= availableWidth)
                    break;
            }
        }

        private void AggregateAllContainersFromGridLogic(GridLogic gridLogic, List<Entry> details)
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
                    var inv = fat.GetInventory(0);
                    if (inv != null)
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

        private Entry GetOrCreateEntry(long entryId)
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

        private void RemoveInactiveEntryModels()
        {
            _entriesToRemove.Clear();
            foreach (var kv in _entryModels)
                if (!_activeEntryIds.Contains(kv.Key))
                    _entriesToRemove.Add(kv.Key);

            for (var i = 0; i < _entriesToRemove.Count; i++)
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

            public long EntryId { get; }
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