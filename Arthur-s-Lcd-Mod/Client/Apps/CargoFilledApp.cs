using System;
using System.Collections.Generic;
using System.Text;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.SurfaceScripts.Abstract;
using LcdMod.Client.Extensions;
using LcdMod.Client.Gui;
using LcdMod.Client.Gui.ControlsTemplates;
using LcdMod.Client.Gui.ControlsTemplates.Panels;
using LcdMod.Client.Gui.ControlsTemplates.Progress;
using LcdMod.Client.Helpers;
using LcdMod.Client.Terminal.Controls;
using LcdMod.Client.Utility;
using LcdMod.Common.Config.Models;
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
        const int ScrollerWidth = 8;
        const int LineHeight = 40;
        const int ScrollDelay = 12;

        readonly List<Entry> _entries = new List<Entry>();
        readonly List<ControlBase> _interactiveList = new List<ControlBase>();
        readonly ScrollPanel _scrollPanel;
        readonly InteractiveSurfaceScript _interactiveHost;
        ScreenConfigWithBlocks Config => (ScreenConfigWithBlocks)AppConfig;
        public bool HasEntries => _entries.Count > 0;
        public List<ControlBase> InteractiveList => _interactiveList;

        public CargoFilledApp(ScreenConfigGeneral config, IAppHost host) : base(config, host)
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
            AggregateAllContainersInLogicalGroup(Host.Block?.CubeGrid, _entries);
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
            var rowHeight = LineHeight * Host.Scale;
            var contentTop = GetContentTop();
            ConfigureScrollPanel(contentTop, rowHeight, _entries.Count);

            BeginScrollPanelClip(sprites);

            int start = _scrollPanel.GetStartIndex(1);
            int renderRows = _scrollPanel.VisibleRows + (_scrollPanel.IsScrollable ? 1 : 0);
            int showCount = Math.Min(renderRows, _entries.Count - start);
            for (int i = 0; i < showCount; i++)
            {
                var bounds = GetListRowBounds(i, _scrollPanel.ContentBounds.Y, _scrollPanel.IsScrollable);
                DrawRow(sprites, _entries[start + i], _scrollPanel.IsScrollable, i, _scrollPanel.ContentBounds.Y);
                AddInteractiveChild(bounds, _entries[start + i]);
            }

            EndScrollPanelClip(sprites);
            RenderScrollPanelBar(sprites);
        }

        void DrawGrid(List<MySprite> sprites)
        {
            var rowHeight = 2f * LineHeight * Host.Scale;
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
                contentEnd -= ScrollerWidth * Host.Scale;
            float columnWidth = (contentEnd - contentStart) / maxCols;
            float gridHeight = maxRows * rowHeight;

            BeginScrollPanelClip(sprites);

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
                DrawGridCell(sprites, _entries[idx], xStart, xEnd, yStart, rowHeight);
                AddInteractiveChild(new RectangleF(xStart, yStart, xEnd - xStart, rowHeight), _entries[idx]);
            }

            EndScrollPanelClip(sprites);
            RenderScrollPanelBar(sprites);
        }

        void DrawRow(List<MySprite> frame, Entry entry, bool showScrollBar, int rowIndex, float contentTop)
        {
            var pct = MathHelper.Clamp(entry.Cap <= 0 ? 0f : (float)(entry.Used / entry.Cap), 0f, 1f);
            float y = contentTop + rowIndex * LineHeight * Host.Scale;
            Vector2 position = new Vector2(Host.ViewBox.Position.X, y);

            if (Config.DrawLines)
                frame.Add(new MySprite { Type = SpriteType.TEXTURE, Data = "SquareSimple", Position = new Vector2(Host.ViewBox.Center.X, position.Y), Size = new Vector2(Host.ViewBox.Width, 2f), Color = Host.ForegroundColor, Alignment = TextAlignment.CENTER });

            var clip = new Rectangle((int)position.X, (int)position.Y, (int)(Host.ViewBox.Width - position.X + Host.ViewBox.X - 145 * Host.Scale), (int)(LineHeight * Host.Scale));
            var barMargin = 8 * Host.Scale;
            Vector2 size = showScrollBar
                ? new Vector2(Host.ViewBox.Width - position.X + Host.ViewBox.X - ScrollerWidth * Host.Scale, clip.Height) - barMargin
                : new Vector2(Host.ViewBox.Width - position.X + Host.ViewBox.X, clip.Height) - barMargin;

            BarPanel.CreateSprites(frame, new Vector2(clip.Location.X, clip.Location.Y + Host.Scale) + barMargin / 2f, size, Config.HeaderColor, Host.BackgroundColor.DeriveAccentColor(), pct, GetEntryUsageColor(pct));
            frame.Add(MySprite.CreateClipRect(clip));
            position.X += 16 * Host.Scale;
            position.Y += 4 * Host.Scale;
            frame.Add(new MySprite { Type = SpriteType.TEXT, Data = entry.Name, Position = position, RotationOrScale = Host.Scale, Color = Host.Surface.ScriptForegroundColor, Alignment = TextAlignment.LEFT, FontId = "White" });
            frame.Add(MySprite.CreateClearClipRect());
            position.X = Host.ViewBox.Width + Host.ViewBox.X - (showScrollBar ? ScrollerWidth * Host.Scale : 0f);
            frame.Add(new MySprite { Type = SpriteType.TEXT, Data = FormatingHelper.PercentageToString(pct), Position = position, RotationOrScale = Host.Scale, Color = Host.Surface.ScriptForegroundColor, Alignment = TextAlignment.RIGHT, FontId = "White" });
        }

        void DrawGridCell(List<MySprite> frame, Entry entry, float xStart, float xEnd, float yStart, float rowHeight)
        {
            var cellPadding = (LineHeight * Host.Scale) / 3f;
            var pct = MathHelper.Clamp(entry.Cap <= 0 ? 0f : (float)(entry.Used / entry.Cap), 0f, 1f);
            var cellView = GetCellViewBox(xStart, xEnd, yStart, rowHeight, cellPadding);

            if (!Config.DrawLines)
            {
                var backgroundColor = Config.HeaderColor;
                var hsv = VRageMath.ColorExtensions.ColorToHSV(backgroundColor);
                hsv.Z *= 0.2f;
                var cellRect = new RectangleF(xStart + cellPadding / 2f, yStart + cellPadding / 2f, (xEnd - xStart) - cellPadding, rowHeight - cellPadding);
                var dropShadow = new RectangleF(cellRect.Position + 2, cellRect.Size);
                Border.CreateSpritesFromRect(dropShadow, frame, hsv.HSVtoColor(), .2f);
                Border.CreateSpritesFromRect(cellRect, frame, backgroundColor, .2f);
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
        }

        void ConfigureScrollPanel(float contentTop, float rowHeight, int totalRows)
        {
            _scrollPanel.Configure(Host.ViewBox, contentTop, 0f, rowHeight, totalRows, ScrollerWidth * Host.Scale, ScrollDelay / 6f);
            _scrollPanel.SetVisible(true);
            if (!_interactiveList.Contains(_scrollPanel))
                _interactiveList.Add(_scrollPanel);
        }

        void RenderScrollPanelBar(List<MySprite> sprites)
        {
            _scrollPanel.RenderScrollBar(
                sprites,
                new Color(Host.Surface.ScriptForegroundColor.R, Host.Surface.ScriptForegroundColor.G, Host.Surface.ScriptForegroundColor.B, 127),
                new Color(Config.HeaderColor.R, Config.HeaderColor.G, Config.HeaderColor.B, 250));
        }

        RectangleF GetListRowBounds(int rowIndex, float contentTop, bool showScrollBar)
        {
            var y = contentTop + rowIndex * LineHeight * Host.Scale;
            var left = Host.ViewBox.Position.X;
            var width = Host.ViewBox.Width - left + Host.ViewBox.X;
            if (showScrollBar)
                width -= ScrollerWidth * Host.Scale;
            return new RectangleF(left, y, width, LineHeight * Host.Scale);
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

        void AddInteractiveChild(RectangleF bounds, object dataContext)
        {
            _scrollPanel.AddChild(new RectangleControl(bounds, CursorType.Default, dataContext)
            {
                CustomRender = RenderNoopControl
            });
        }

        static void RenderNoopControl(ControlBase control, ControlRenderContext context, List<MySprite> sprites)
        {
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

        static int GetScrollStep(float secondsPerStep)
        {
            try
            {
                var sess = MyAPIGateway.Session;
                if (sess == null)
                    return 0;
                if (secondsPerStep <= 0f)
                    secondsPerStep = 1f / 60f;
                int ticksPerStep = Math.Max(1, (int)Math.Round(secondsPerStep * 60f));
                return (int)(sess.GameplayFrameCounter / ticksPerStep);
            }
            catch
            {
                return 0;
            }
        }

        void DrawScrollBar(List<MySprite> frame, float scale, float initialY, float viewportHeight, float scrollBarCenter, float scrollBarHeight)
        {
            float barXCenter = Host.ViewBox.X + Host.ViewBox.Width - (ScrollerWidth / 2f) * scale;
            int barWidth = (int)(ScrollerWidth * scale);

            var trackCenter = new Vector2(barXCenter, (float)Math.Round(initialY + viewportHeight / 2f, MidpointRounding.ToEven));
            DrawCapsule(frame, trackCenter, barWidth, viewportHeight,
                new Color(Host.Surface.ScriptForegroundColor.R, Host.Surface.ScriptForegroundColor.G, Host.Surface.ScriptForegroundColor.B, 127));

            var thumbCenter = new Vector2(barXCenter, (float)Math.Round(initialY + scrollBarCenter, MidpointRounding.ToEven));
            DrawCapsule(frame, thumbCenter, barWidth, scrollBarHeight,
                new Color(Config.HeaderColor.R, Config.HeaderColor.G, Config.HeaderColor.B, 250));
        }

        static void DrawCapsule(List<MySprite> frame, Vector2 center, int width, float height, Color color)
        {
            frame.Add(new MySprite
            {
                Type = SpriteType.TEXTURE,
                Data = "SquareSimple",
                Position = center,
                Size = new Vector2(width, height + .5f),
                Color = color,
                Alignment = TextAlignment.CENTER
            });

            var capsSize = new Vector2(width);
            frame.Add(new MySprite
            {
                Type = SpriteType.TEXTURE,
                Data = "SemiCircle",
                Position = new Vector2(center.X, center.Y - height / 2f),
                Size = capsSize,
                RotationOrScale = 0f,
                Color = color,
                Alignment = TextAlignment.CENTER
            });

            frame.Add(new MySprite
            {
                Type = SpriteType.TEXTURE,
                Data = "SemiCircle",
                Position = new Vector2(center.X, center.Y + height / 2f),
                Size = capsSize,
                RotationOrScale = (float)Math.PI,
                Color = color,
                Alignment = TextAlignment.CENTER
            });
        }

        void AggregateAllContainersInLogicalGroup(IMyCubeGrid rootGrid, List<Entry> details)
        {
            if (rootGrid == null)
                return;

            var grids = new List<IMyCubeGrid>();
            try
            {
                MyAPIGateway.GridGroups.GetGroup(rootGrid, GridLinkTypeEnum.Logical, grids);
            }
            catch (Exception e)
            {
                ErrorHandlerHelper.LogError(e, Host);
            }

            var hasRoot = false;
            for (var i = 0; i < grids.Count; i++)
            {
                if (grids[i] != rootGrid)
                    continue;
                hasRoot = true;
                break;
            }

            if (!hasRoot)
                grids.Insert(0, rootGrid);

            var slims = new List<IMySlimBlock>();
            for (var gi = 0; gi < grids.Count; gi++)
            {
                var g = grids[gi];
                if (g == null)
                    continue;

                slims.Clear();
                g.GetBlocks(slims);

                for (var i = 0; i < slims.Count; i++)
                {
                    var fat = slims[i].FatBlock as IMyTerminalBlock;
                    if (fat == null)
                        continue;

                    var typeIdStr = string.Empty;
                    try
                    {
                        typeIdStr = fat.BlockDefinition.TypeIdString ?? fat.BlockDefinition.TypeId.ToString();
                    }
                    catch (Exception e)
                    {
                        ErrorHandlerHelper.LogError(e, Host);
                    }

                    if (typeIdStr.IndexOf("CargoContainer", StringComparison.OrdinalIgnoreCase) < 0)
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

                    details.Add(new Entry { Name = name, Used = localUsed, Cap = localCap });
                }
            }
        }

        public class Entry
        {
            public double Cap;
            public string Name;
            public double Used;
        }
    }
}
