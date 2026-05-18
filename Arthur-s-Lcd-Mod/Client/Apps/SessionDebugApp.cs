using System.Collections.Generic;
using System.Linq;
using System.Text;
using LcdMod.Client.SurfaceScripts;
using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace LcdMod.Client.Apps
{
    internal sealed class SessionDebugApp
    {
        const string DebugFont = "Monospace";
        const float LineScale = 0.62f;
        static readonly Color RunningColor = new Color(80, 220, 120);
        static readonly Color IdleColor = new Color(230, 90, 90);
        static readonly Color SleepingColor = new Color(235, 210, 60);
        readonly List<MySprite> _sprites = new List<MySprite>();

        public List<MySprite> GetSprites(SessionDebugSurfaceScript owner)
        {
            var viewBox = GetViewBox(owner);
            var snapshot = LcdModSessionComponent.DebugSnapshot;
            var lines = BuildDebugLines(snapshot, owner);
            var lineHeight = owner.Surface.MeasureStringInPixels(new StringBuilder("A"), DebugFont, LineScale).Y + 2f;
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
                    FontId = DebugFont,
                    Alignment = TextAlignment.LEFT,
                    RotationOrScale = LineScale
                });
            }

            return _sprites;
        }

        static RectangleF GetViewBox(SessionDebugSurfaceScript owner)
        {
            var sizeOffset = (owner.Surface.TextureSize - owner.Surface.SurfaceSize) / 2f;
            var padding = (owner.Surface.TextPadding / 100f) * owner.Surface.SurfaceSize;
            sizeOffset += padding / 2f;
            return new RectangleF(sizeOffset.X, sizeOffset.Y, owner.Surface.SurfaceSize.X - padding.X, owner.Surface.SurfaceSize.Y - padding.Y);
        }

        static List<DebugLine> BuildDebugLines(SessionDebugSnapshot snapshot, SessionDebugSurfaceScript owner)
        {
            var lines = new List<DebugLine>(32);
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
            lines.Add(new DebugLine("Name                   ite  prc  bat  nxt", Color.White));

            var components = LcdModSessionComponent.Components;
            if (components == null || components.Count == 0)
            {
                lines.Add(new DebugLine("  (none)", Color.White));
                return lines;
            }

            int shown = 0;
            foreach (var pair in components.Where(p => p.Value != null && p.Value.Grid != null).OrderByDescending(p => p.Value.LastRefreshIterations).ThenBy(p => p.Key))
            {
                if (shown >= 32)
                {
                    lines.Add(new DebugLine("-- More " + (components.Count - shown) + " --", Color.White));
                    break;
                }

                var logic = pair.Value;
                if (logic == null)
                    continue;

                var gridName = ClampToWidth(logic.Grid != null ? logic.Grid.CustomName ?? string.Empty : string.Empty, 22);
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
