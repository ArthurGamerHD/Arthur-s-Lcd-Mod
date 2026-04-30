using System.Collections.Generic;
using System.Linq;
using System.Text;
using Generated;
using Graph.Apps.Abstract;
using Graph.System;
using Sandbox.Game.Components;
using Sandbox.Game.Entities;
using Sandbox.Game.GameSystems.TextSurfaceScripts;
using Sandbox.ModAPI;
using VRage.Collections;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI;
using VRageMath;

namespace Graph.Apps
{
#if DEBUG
    [MyTextSurfaceScript(ID, TITLE)]
#endif
    public partial class SessionDebugSurfaceScript : SurfaceScriptBase
    {
        protected override ConfigKind ConfigKind => ConfigKind.Colorable;

        public const string ID = "SessionDebug";
        public const string TITLE = "LCDMod Session Debug";
        const string DEBUG_FONT = "Monospace";
        const float LINE_SCALE = 0.62f;
        static readonly Color RunningColor = new Color(80, 220, 120);
        static readonly Color IdleColor = new Color(230, 90, 90);
        static readonly Color SleepingColor = new Color(235, 210, 60);

        List<MySprite> _sprites = new List<MySprite>();
        public override ScriptUpdate NeedsUpdate => ScriptUpdate.Update10;

        public SessionDebugSurfaceScript(IMyTextSurface surface, IMyCubeBlock block, Vector2 size)
            : base(surface, block, size)
        {
            LcdModSessionComponent.OnAfterSimulationUpdate += HandleAfterSimulationUpdate;
        }

        public override void Dispose()
        {
            LcdModSessionComponent.OnAfterSimulationUpdate -= HandleAfterSimulationUpdate;
            base.Dispose();
        }

        void HandleAfterSimulationUpdate()
        {
            if (Surface == null)
                return;

            RenderSprites(DrawApp);
        }

        ListReader<MySprite> DrawApp()
        {
            var viewBox = GetViewBox();
            var snapshot = LcdModSessionComponent.DebugSnapshot;
            var lines = BuildDebugLines(snapshot);
            var lineHeight = Surface.MeasureStringInPixels(new StringBuilder("A"), DEBUG_FONT, LINE_SCALE).Y + 2f;
            var start = viewBox.Position + new Vector2(8f, 8f);

            _sprites.Clear();
            
            for (int i = 0; i < lines.Count; i++)
            {
                _sprites.Add(new MySprite
                {
                    Type = SpriteType.TEXT,
                    Data = lines[i].Text,
                    Position = start + new Vector2(0f, i * lineHeight),
                    Color = lines[i].Color,
                    FontId = DEBUG_FONT,
                    Alignment = TextAlignment.LEFT,
                    RotationOrScale = LINE_SCALE
                });
            }
            return _sprites;
        }

        RectangleF GetViewBox()
        {
            var sizeOffset = (Surface.TextureSize - Surface.SurfaceSize) / 2f;
            var padding = (Surface.TextPadding / 100f) * Surface.SurfaceSize;
            sizeOffset += padding / 2f;
            return new RectangleF(sizeOffset.X, sizeOffset.Y, Surface.SurfaceSize.X - padding.X,
                Surface.SurfaceSize.Y - padding.Y);
        }

        List<DebugLine> BuildDebugLines(SessionDebugSnapshot snapshot)
        {
            var lines = new List<DebugLine>(32);
            lines.Add(new DebugLine($"LCDMod Session Debug - {GetHashCode()}", Color.White));
            lines.Add(new DebugLine("Tick: " + Fixed4(snapshot.UpdateTick), Color.White));
            lines.Add(new DebugLine("Tracked Grids: " + Fixed4(snapshot.TrackedGrids), Color.White));
            lines.Add(new DebugLine("GridLogic Entries: " + Fixed4(snapshot.TrackedGridLogic), Color.White));
            lines.Add(new DebugLine("Refresh In Progress: " + Fixed4(snapshot.RefreshInProgress), Color.White));
            lines.Add(new DebugLine("Last Iterations Sum: " + Fixed4(snapshot.TotalLastRefreshIterations),
                Color.White));
            lines.Add(new DebugLine("Last Processed Sum: " + Fixed4(snapshot.TotalLastRefreshProcessed), Color.White));
            lines.Add(new DebugLine("Avg Next Batch: " + Fixed4(snapshot.AverageNextBatchSize), Color.White));
            lines.Add(new DebugLine(string.Empty, Color.White));
            lines.Add(new DebugLine("Per Grid:", Color.White));
            lines.Add(new DebugLine("Name                   ite  prc  bat  nxt", Color.White));

            var components = LcdModSessionComponent.Components;
            if (components == null || components.Count == 0)
            {
                lines.Add(new DebugLine("  (none)", Color.White));
                return lines;
            }

            int shown = 0;
            foreach (var pair in components
                         .Where(p => p.Value != null && p.Value.Grid != null)
                         .OrderByDescending(p => p.Value.LastRefreshIterations)
                         .ThenBy(p => p.Key))
            {
                if (shown >= 32)
                {
                    lines.Add(new DebugLine($"-- More {components.Count - shown} --", Color.White));
                    break;
                }


                var logic = pair.Value;
                if (logic == null)
                    continue;

                var gridName = ClampToWidth(logic.Grid?.CustomName ?? string.Empty, 22);
                lines.Add(new DebugLine(
                    gridName + " "
                             + Fixed4(logic.LastRefreshIterations) + " "
                             + Fixed4(logic.LastRefreshProcessed) + " "
                             + Fixed4(logic.CurrentRefreshBatchSize) + " "
                             + Fixed4(logic.EstimatedNextRefreshBatchSize),
                    logic.IsSleeping ? SleepingColor : (logic.IsRefreshRunning ? RunningColor : IdleColor)));

                shown++;
            }

            lines.Add(new DebugLine(string.Empty, Color.White));
            lines.Add(new DebugLine("Modules:", Color.White));
            lines.Add(new DebugLine("Name                   cnt  act", Color.White));
            var moduleLines = snapshot.ModuleLines;
            if (moduleLines == null || moduleLines.Length == 0)
            {
                lines.Add(new DebugLine("  (none)", Color.White));
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
    }
}
