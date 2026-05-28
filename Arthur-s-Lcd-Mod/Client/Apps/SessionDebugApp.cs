using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.Grid;
using LcdMod.Client.Gui.ControlsTemplates;
using LcdMod.Client.Gui.ControlsTemplates.Panels;
using LcdMod.Client.SurfaceScripts;
using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace LcdMod.Client.Apps
{
    internal sealed class SessionDebugApp : IApp
    {
        const string DEBUG_FONT = "Monospace";
        const float LINE_SCALE = 0.62f;
        const float INNER_PADDING = 8f;
        const float SCROLLBAR_WIDTH = 8f;

        static readonly Color RunningColor = new Color(80, 220, 120);
        static readonly Color IdleColor = new Color(230, 90, 90);
        static readonly Color SleepingColor = new Color(235, 210, 60);

        readonly List<MySprite> _sprites = new List<MySprite>();
        readonly ScrollPanel _scrollPanel = new ScrollPanel();
        readonly List<ControlBase> _interactiveEntries = new List<ControlBase>();

        public SessionDebugApp()
        {
            _scrollPanel.ManualScrollInertiaEnabled = false;
            _interactiveEntries.Add(_scrollPanel);
        }

        public List<ControlBase> InteractiveEntries
        {
            get { return _interactiveEntries; }
        }

        public List<MySprite> GetSprites(SessionDebugSurfaceScript owner)
        {
            var viewBox = GetViewBox(owner);
            var snapshot = LcdModSessionComponent.DebugSnapshot;
            var lines = BuildDebugLines(snapshot, owner);
            var lineHeight = owner.Surface.MeasureStringInPixels(new StringBuilder("A"), DEBUG_FONT, LINE_SCALE).Y + 2f;

            var contentViewBox = new RectangleF(
                viewBox.X + INNER_PADDING,
                viewBox.Y + INNER_PADDING,
                Math.Max(0f, viewBox.Width - INNER_PADDING * 2f),
                Math.Max(0f, viewBox.Height - INNER_PADDING * 2f));

            _scrollPanel.ClearChildren();
            _scrollPanel.Configure(
                contentViewBox,
                contentViewBox.Y,
                0f,
                lineHeight,
                lines.Count,
                SCROLLBAR_WIDTH,
                0f);
            _scrollPanel.SetScrollBarColors(
                new Color(45, 45, 45, 170),
                new Color(190, 190, 190, 230));

            _sprites.Clear();

            BeginClip(_sprites, _scrollPanel.ContentViewportBounds);

            int startRow = _scrollPanel.StartRow;
            int endRow = Math.Min(lines.Count, startRow + _scrollPanel.RenderRows);

            for (int i = startRow; i < endRow; i++)
            {
                var line = lines[i];
                int visibleIndex = i - startRow;

                _sprites.Add(new MySprite
                {
                    Type = SpriteType.TEXT,
                    Data = line.Text,
                    Position = new Vector2(
                        _scrollPanel.ContentViewportBounds.X,
                        _scrollPanel.ContentBounds.Y + visibleIndex * lineHeight),
                    Color = line.Color,
                    FontId = DEBUG_FONT,
                    Alignment = TextAlignment.LEFT,
                    RotationOrScale = LINE_SCALE
                });
            }

            EndClip(_sprites);

            _scrollPanel.Render(
                new ControlRenderContext(owner.Surface, 1f, 1f, Color.White, Color.Transparent, new Vector2(float.NaN, float.NaN)),
                _sprites);

            return _sprites;
        }

        static RectangleF GetViewBox(SessionDebugSurfaceScript owner)
        {
            var sizeOffset = (owner.Surface.TextureSize - owner.Surface.SurfaceSize) / 2f;
            var padding = (owner.Surface.TextPadding / 100f) * owner.Surface.SurfaceSize;
            sizeOffset += padding / 2f;
            return new RectangleF(sizeOffset.X, sizeOffset.Y, owner.Surface.SurfaceSize.X - padding.X, owner.Surface.SurfaceSize.Y - padding.Y);
        }

        static void BeginClip(List<MySprite> sprites, RectangleF bounds)
        {
            sprites.Add(new MySprite
            {
                Type = SpriteType.CLIP_RECT,
                Position = bounds.Position,
                Size = bounds.Size,
                Alignment = TextAlignment.LEFT
            });
        }

        static void EndClip(List<MySprite> sprites)
        {
            sprites.Add(MySprite.CreateClearClipRect());
        }

        static List<DebugLine> BuildDebugLines(SessionDebugSnapshot snapshot, SessionDebugSurfaceScript owner)
        {
            var lines = new List<DebugLine>(64);

            lines.Add(new DebugLine("LcdMod Session Debug - " + owner.GetHashCode(), Color.White));
            lines.Add(new DebugLine("Tick: " + Fixed4(snapshot.UpdateTick), Color.White));
            lines.Add(new DebugLine("Tracked Grids: " + Fixed4(snapshot.TrackedGrids), Color.White));
            lines.Add(new DebugLine("GridLogic Entries: " + Fixed4(snapshot.TrackedGridLogic), Color.White));
            lines.Add(new DebugLine("Refresh In Progress: " + Fixed4(snapshot.RefreshInProgress), Color.White));
            lines.Add(new DebugLine("Last Iterations Sum: " + Fixed4(snapshot.TotalLastRefreshIterations), Color.White));
            lines.Add(new DebugLine("Last Processed Sum: " + Fixed4(snapshot.TotalLastRefreshProcessed), Color.White));
            lines.Add(new DebugLine("Avg Next Batch: " + Fixed4(snapshot.AverageNextBatchSize), Color.White));
            lines.Add(new DebugLine(string.Empty, Color.White));
            lines.Add(new DebugLine("Per Grid:", Color.White));
            lines.Add(new DebugLine("Name ite prc bat nxt", Color.White));

            var components = LcdModSessionComponent.Components;
            if (components == null || components.Count == 0)
            {
                lines.Add(new DebugLine(" (none)", Color.White));
                return lines;
            }

            foreach (var pair in components
                         .Where(p => p.Value != null && p.Value.Grid != null)
                         .OrderByDescending(p => p.Value.LastRefreshIterations)
                         .ThenBy(p => p.Key))
            {
                var logic = pair.Value;
                if (logic == null)
                    continue;

                var gridName = ClampToWidth(logic.Grid != null ? logic.Grid.CustomName ?? string.Empty : string.Empty, 22);
                lines.Add(new DebugLine(
                    gridName + " " +
                    Fixed4(logic.LastRefreshIterations) + " " +
                    Fixed4(logic.LastRefreshProcessed) + " " +
                    Fixed4(logic.CurrentRefreshBatchSize) + " " +
                    Fixed4(logic.EstimatedNextRefreshBatchSize),
                    logic.IsSleeping ? SleepingColor : (logic.IsRefreshRunning ? RunningColor : IdleColor)));
            }

            lines.Add(new DebugLine(string.Empty, Color.White));
            lines.Add(new DebugLine("Modules:", Color.White));
            lines.Add(new DebugLine("Name cnt act", Color.White));

            var moduleLines = snapshot.ModuleLines;
            if (moduleLines == null || moduleLines.Length == 0)
            {
                lines.Add(new DebugLine(" (none)", Color.White));
                return lines;
            }

            for (int i = 0; i < moduleLines.Length; i++)
            {
                string moduleName;
                int count;
                int active;
                ParseModuleLine(moduleLines[i], out moduleName, out count, out active);
                moduleName = ClampToWidth(moduleName, 22);
                lines.Add(new DebugLine(moduleName + " " + Fixed4(count) + " " + Fixed4(active), Color.White));
            }
            
#if EXPERIMENTAL
            lines.Add(new DebugLine(string.Empty, Color.White));
            lines.Add(new DebugLine("Actions:", Color.White));

            foreach (var action in GridLogic.TerminalActions)
            {
                lines.Add(new DebugLine("   " + action.Key, Color.White));
            }

            lines.Add(new DebugLine(string.Empty, Color.White));
            lines.Add(new DebugLine("Properties:", Color.White));

            foreach (var property in GridLogic.TerminalProperties)
            {
                lines.Add(new DebugLine("   " + property.Key, Color.White));
            }
#endif
            
            return lines;
        }

        static string ClampToWidth(string value, int width)
        {
            if (string.IsNullOrEmpty(value))
                return new string(' ', width);

            if (value.Length > width)
                return value.Substring(0, width);

            return value.PadRight(width);
        }

        static string Fixed4(int value)
        {
            return ClampToWidth(value.ToString(), 4);
        }

        static void ParseModuleLine(string line, out string moduleName, out int count, out int active)
        {
            moduleName = string.Empty;
            count = 0;
            active = 0;

            if (string.IsNullOrEmpty(line))
                return;

            var parts = line.Split(':');
            if (parts.Length == 0)
                return;

            moduleName = parts[0].Trim();

            if (parts.Length > 1)
            {
                int parsedCount;
                if (int.TryParse(parts[1].Trim(), out parsedCount))
                    count = parsedCount;
            }

            if (parts.Length > 2)
            {
                int parsedActive;
                if (int.TryParse(parts[2].Trim(), out parsedActive))
                    active = parsedActive;
            }
        }

        struct DebugLine
        {
            public readonly string Text;
            public readonly Color Color;

            public DebugLine(string text, Color color)
            {
                Text = text;
                Color = color;
            }
        }

        public void Update()
        {
        }

        public void LayoutChanged()
        {
        }

        public List<MySprite> GetSprites()
        {
            return new List<MySprite>();
        }
    }
}
