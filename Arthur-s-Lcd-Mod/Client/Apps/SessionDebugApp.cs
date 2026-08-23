using LcdMod.Common.Config.Components;
using System;
using System.Collections.Generic;
using System.Linq;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.Gui;
using LcdMod.Client.Gui.ControlsTemplates;
using LcdMod.Client.Gui.ControlsTemplates.Panels;
using LcdMod.Client.Gui.ControlsTemplates.Panels.Virtualized;
using LcdMod.Client.Helpers;
using LcdMod.Client.SurfaceScripts;
using VRage.Game.GUI.TextPanel;
using VRageMath;

using LcdMod.Common.Config.Generation;
namespace LcdMod.Client.Apps
{
    [LcdApp(21)]
    // ReSharper disable once PartialTypeWithSinglePart
    internal sealed partial class SessionDebugApp : App
    {
        const string DEBUG_FONT = "Monospace";
        const float LINE_SCALE = 0.62f;
        const float INNER_PADDING = 8f;
        const float SCROLLBAR_WIDTH = 8f;

        static readonly Color RunningColor = new Color(80, 220, 120);
        static readonly Color IdleColor = new Color(230, 90, 90);
        static readonly Color SleepingColor = new Color(235, 210, 60);

        readonly List<MySprite> _sprites = new List<MySprite>();
        readonly ScrollPanel _scrollPanel;
        readonly VirtualizedStackPanel<DebugLine> _linePanel = new VirtualizedStackPanel<DebugLine>();
        readonly List<Control> _interactiveEntries = new List<Control>();

        readonly SessionDebugSurfaceScript _host;
        
        public SessionDebugApp(SessionDebugSurfaceScript sessionDebugSurfaceScript) : base(sessionDebugSurfaceScript)
        {
            _scrollPanel = AddLogicalChild(new ScrollPanel());
            _scrollPanel.ManualScrollInertiaEnabled = false;
            _interactiveEntries.Add(_scrollPanel);
            _linePanel.CreateControl = CreateLineControl;
            _linePanel.BindControl = BindLineControl;
            _host = sessionDebugSurfaceScript;
        }

        public List<Control> InteractiveEntries => _interactiveEntries;

        float Scale => GeneralComponent.GetScale();

        float FontScale => _host != null && _host.Surface != null ? Math.Max(0.1f, _host.Surface.FontSize) : 1f;

        float TextScale => LINE_SCALE * Scale * FontScale;

        public override List<MySprite> GetSprites()
        {
            var viewBox = Host.ViewBox;
            var snapshot = LcdModSessionComponent.DebugSnapshot;
            var lines = BuildDebugLines(snapshot, _host);
            var lineHeight = TextWrappingHelper.GetLineHeight(
                _host.Surface,
                DEBUG_FONT,
                TextScale,
                2f * Math.Max(0.5f, Scale * FontScale));

            var contentViewBox = new RectangleF(
                viewBox.X + INNER_PADDING,
                viewBox.Y + INNER_PADDING,
                Math.Max(0f, viewBox.Width - INNER_PADDING * 2f),
                Math.Max(0f, viewBox.Height - INNER_PADDING * 2f));

            _scrollPanel.SetContent(_linePanel);
            _linePanel.ItemsSource = lines;
            _linePanel.RowHeight = lineHeight;
            _linePanel.Gap = 0f;
            _scrollPanel.AutoScrollSecondsPerStep = 0f;
            _scrollPanel.ConfigureAutomatic(
                contentViewBox,
                SCROLLBAR_WIDTH,
                lineHeight);

            _sprites.Clear();
            _scrollPanel.Render(_sprites);

            ClearDirtyAfterRender();
            return _sprites;
        }

        ControlTemplate CreateLineControl(DebugLine line)
        {
            return new RectangleControl(default(RectangleF), CursorType.Default, line)
            {
                CustomRender = RenderLineControl
            };
        }

        static void BindLineControl(ControlTemplate control, DebugLine line, int index)
        {
            if (control != null)
                control.SetDataContext(line);
        }

        void RenderLineControl(ControlTemplate control, List<MySprite> sprites)
        {
            if (control == null || sprites == null)
                return;

            var line = control.DataContext as DebugLine? ?? new DebugLine(string.Empty, Color.White);
            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT,
                Data = line.Text,
                Position = control.Bounds.Position,
                Color = line.Color,
                FontId = DEBUG_FONT,
                Alignment = TextAlignment.LEFT,
                RotationOrScale = TextScale
            });
        }

        static List<DebugLine> BuildDebugLines(SessionDebugSnapshot snapshot, SessionDebugSurfaceScript owner)
        {
            var lines = new List<DebugLine>(64)
            {
                new DebugLine("LcdMod Session Debug - " + owner.GetHashCode(), Color.White),
                new DebugLine($"Screen Info: {owner.Surface.Name} - {owner.Surface.TextureSize} - {owner.ViewBox}",
                    Color.White),
                new DebugLine("Tick: " + Fixed4(snapshot.UpdateTick), Color.White),
                new DebugLine("Tracked Grids: " + Fixed4(snapshot.TrackedGrids), Color.White),
                new DebugLine("GridLogic Entries: " + Fixed4(snapshot.TrackedGridLogic), Color.White),
                new DebugLine("Refresh In Progress: " + Fixed4(snapshot.RefreshInProgress), Color.White),
                new DebugLine("Last Iterations Sum: " + Fixed4(snapshot.TotalLastRefreshIterations), Color.White),
                new DebugLine("Last Processed Sum: " + Fixed4(snapshot.TotalLastRefreshProcessed), Color.White),
                new DebugLine("Avg Next Batch: " + Fixed4(snapshot.AverageNextBatchSize), Color.White),
                new DebugLine(string.Empty, Color.White),
                new DebugLine("Per Grid:", Color.White),
                new DebugLine("Name blk itm", Color.White)
            };

            var components = LcdModSessionComponent.Components;
            if (components == null || components.Count == 0)
            {
                lines.Add(new DebugLine(" (none)", Color.White));
                return lines;
            }

            foreach (var pair in components
                         .Where(p => p.Value != null && p.Value.Grid != null)
                         .OrderByDescending(p => p.Value.TrackedBlockCount)
                         .ThenBy(p => p.Key))
            {
                var logic = pair.Value;
                if (logic == null)
                    continue;

                var gridName = ClampToWidth(logic.Grid != null ? logic.Grid.CustomName ?? string.Empty : string.Empty, 22);
                lines.Add(new DebugLine(
                    gridName + " " +
                    Fixed4(logic.TrackedBlockCount) + " " +
                    Fixed4(logic.TrackedItemCount),
                    IdleColor));
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

            foreach (var line in moduleLines)
            {
                string moduleName;
                int count;
                int active;
                ParseModuleLine(line, out moduleName, out count, out active);
                moduleName = ClampToWidth(moduleName, 22);
                lines.Add(new DebugLine(moduleName + " " + Fixed4(count) + " " + Fixed4(active), Color.White));
            }
            
            
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

        public override void Update()
        {
        }

        // hack: not needed on this app
        public override IReadOnlyList<Control> VisualChildren { get; } = new Control[]{};
    }
}
