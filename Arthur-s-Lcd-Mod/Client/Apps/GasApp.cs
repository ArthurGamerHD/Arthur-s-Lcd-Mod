using System;
using System.Collections.Generic;
using System.Text;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.Extensions;
using LcdMod.Client.Gui.Controls;
using LcdMod.Client.Helpers;
using LcdMod.Client.Terminal.Controls;
using LcdMod.Client.Utility;
using LcdMod.Common.Config.Models;
using LcdMod.Common.Helpers;
using Sandbox.Definitions;
using Sandbox.ModAPI;
using VRage;
using VRage.Game;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI;
using VRage.Game.ObjectBuilders.Definitions;
using VRageMath;
using IMyCubeGrid = VRage.Game.ModAPI.IMyCubeGrid;
using IMySlimBlock = VRage.Game.ModAPI.IMySlimBlock;
using IMyTerminalBlock = Sandbox.ModAPI.IMyTerminalBlock;

namespace LcdMod.Client.Apps
{
    public sealed class GasApp : AppBase
    {
        const int ScrollerWidth = 8;
        const int LineHeight = 40;
        const int ScrollDelay = 12;

        readonly Dictionary<string, string> _gasDisplayNameCache =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        readonly List<Entry> _entries = new List<Entry>();

        ScreenConfigColorable Config => (ScreenConfigColorable)AppConfig;
        public bool HasEntries => _entries.Count > 0;

        public GasApp(ScreenConfigGeneral config, SurfaceScriptBase host) : base(config, host)
        {
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
            var viewportAvailableHeight = Host.ViewBox.Height - (GetContentTop() - Host.ViewBox.Y);
            int maxRows = Math.Max(1, (int)Math.Floor(viewportAvailableHeight / rowHeight));
            bool shouldScroll = _entries.Count > maxRows;

            int start = 0;
            if (shouldScroll)
            {
                int totalSteps = Math.Max(1, _entries.Count - maxRows);
                int step = GetScrollStep(ScrollDelay / 6f);
                start = step % (totalSteps + 1);

                float viewportHeight = maxRows * rowHeight - (ScrollerWidth * 2 * Host.Scale);
                float scrollBarHeight = (float)maxRows / _entries.Count * viewportHeight;
                float totalScrollableRows = _entries.Count - maxRows;
                float scrollFraction = totalScrollableRows > 0 ? start / totalScrollableRows : 0f;
                float scrollBarTravel = viewportHeight - scrollBarHeight;
                float scrollBarY = scrollFraction * scrollBarTravel;
                float scrollBarCenter = scrollBarY + scrollBarHeight / 2f;
                float initialY = GetContentTop() + ScrollerWidth * Host.Scale;
                DrawScrollBar(sprites, Host.Scale, initialY, viewportHeight, scrollBarCenter, scrollBarHeight);
            }

            int showCount = Math.Min(maxRows, _entries.Count - start);
            for (int i = 0; i < showCount; i++)
                DrawRow(sprites, _entries[start + i], shouldScroll, i);
        }

        void DrawGrid(List<MySprite> sprites)
        {
            var rowHeight = 2f * LineHeight * Host.Scale;
            var viewportAvailableHeight = Host.ViewBox.Height - (GetContentTop() - Host.ViewBox.Y);
            int maxRows = Math.Max(1, (int)Math.Floor(viewportAvailableHeight / rowHeight));
            int maxCols = 1; // Keep previous Gas behavior.
            int maxVisible = maxRows * maxCols;
            bool shouldScroll = _entries.Count > maxVisible;
            int startRow = 0;

            if (shouldScroll)
            {
                int totalRows = (int)Math.Ceiling(_entries.Count / (float)maxCols);
                int totalSteps = Math.Max(1, totalRows - maxRows);
                int step = GetScrollStep(ScrollDelay / 6f);
                startRow = step % (totalSteps + 1);

                float viewportHeight = maxRows * rowHeight - (ScrollerWidth * 2 * Host.Scale);
                float scrollBarHeight = (float)maxRows / totalRows * viewportHeight;
                float totalScrollableRows = totalRows - maxRows;
                float scrollFraction = totalScrollableRows > 0 ? startRow / totalScrollableRows : 0f;
                float scrollBarTravel = viewportHeight - scrollBarHeight;
                float scrollBarY = scrollFraction * scrollBarTravel;
                float scrollBarCenter = scrollBarY + scrollBarHeight / 2f;
                float initialY = GetContentTop() + ScrollerWidth * Host.Scale;
                DrawScrollBar(sprites, Host.Scale, initialY, viewportHeight, scrollBarCenter, scrollBarHeight);
            }

            int start = startRow * maxCols;
            int showCount = Math.Min(maxVisible, _entries.Count - start);
            float contentStart = Host.ViewBox.X;
            float contentEnd = Host.ViewBox.Width + Host.ViewBox.X;
            if (shouldScroll)
                contentEnd -= ScrollerWidth * Host.Scale;
            float columnWidth = (contentEnd - contentStart) / maxCols;
            float gridHeight = maxRows * rowHeight;

            if (Config.DrawLines)
            {
                var lineColor = Config.HeaderColor;
                for (int row = 0; row <= maxRows; row++)
                {
                    var y = GetContentTop() + row * rowHeight;
                    sprites.Add(new MySprite { Type = SpriteType.TEXTURE, Data = "SquareSimple", Position = new Vector2((contentStart + contentEnd) / 2f, y), Size = new Vector2(contentEnd - contentStart, 2f), Color = lineColor, Alignment = TextAlignment.CENTER });
                }

                for (int col = 0; col <= maxCols; col++)
                {
                    var x = contentStart + col * columnWidth;
                    sprites.Add(new MySprite { Type = SpriteType.TEXTURE, Data = "SquareSimple", Position = new Vector2(x, GetContentTop() + gridHeight / 2f), Size = new Vector2(2f, gridHeight), Color = lineColor, Alignment = TextAlignment.CENTER });
                }
            }

            for (int gridIdx = 0; gridIdx < showCount; gridIdx++)
            {
                int idx = start + gridIdx;
                int col = gridIdx % maxCols;
                int row = gridIdx / maxCols;
                float xStart = contentStart + col * columnWidth;
                float xEnd = (col == maxCols - 1) ? contentEnd : xStart + columnWidth;
                float yStart = GetContentTop() + row * rowHeight;
                DrawGridCell(sprites, _entries[idx], xStart, xEnd, yStart, rowHeight);
            }
        }

        void DrawRow(List<MySprite> frame, Entry entry, bool showScrollBar, int rowIndex)
        {
            var pct = MathHelper.Clamp(entry.Percentage, 0f, 1f);
            float y = GetContentTop() + rowIndex * LineHeight * Host.Scale;
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
            var pct = MathHelper.Clamp(entry.Percentage, 0f, 1f);
            var cellView = GetCellViewBox(xStart, xEnd, yStart, rowHeight, cellPadding);

            if (!Config.DrawLines)
            {
                var backgroundColor = Config.HeaderColor;
                var hsv = VRageMath.ColorExtensions.ColorToHSV(backgroundColor);
                hsv.Z *= 0.2f;
                var cellRect = new RectangleF(xStart + cellPadding / 2f, yStart + cellPadding / 2f, (xEnd - xStart) - cellPadding, rowHeight - cellPadding);
                var dropShadow = new RectangleF(cellRect.Position + 2, cellRect.Size);
                RectanglePanel.CreateSpritesFromRect(dropShadow, frame, hsv.HSVtoColor(), .2f);
                RectanglePanel.CreateSpritesFromRect(cellRect, frame, backgroundColor, .2f);
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

        void ReadEntries(List<Entry> entries)
        {
            ReadEntries(Host.Block as IMyTerminalBlock, entries, Host.GetType());
        }

        void ReadEntries(IMyTerminalBlock sourceBlock, List<Entry> entries, Type logType)
        {
            string mode;
            string token;
            ParseFilter(sourceBlock, out mode, out token);

            var rootGrid = sourceBlock?.CubeGrid;
            if (rootGrid == null)
                return;

            var grids = new List<IMyCubeGrid>();
            try
            {
                MyAPIGateway.GridGroups.GetGroup(rootGrid, GridLinkTypeEnum.Logical, grids);
            }
            catch (Exception e)
            {
                ErrorHandlerHelper.LogError(e, logType);
            }

            if (grids.Count == 0)
                grids.Add(rootGrid);

            var slims = new List<IMySlimBlock>();
            for (var g = 0; g < grids.Count; g++)
            {
                var grid = grids[g];
                if (grid == null)
                    continue;

                slims.Clear();
                grid.GetBlocks(slims);

                for (var i = 0; i < slims.Count; i++)
                {
                    var tank = slims[i].FatBlock as IMyGasTank;
                    if (tank == null)
                        continue;

                    var terminal = tank as IMyTerminalBlock;

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
