using System.Collections.Generic;
using System.Linq;
using System.Text;
using Graph.System;
using Sandbox.Game.GameSystems.TextSurfaceScripts;
using Sandbox.ModAPI;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI;
using VRageMath;

namespace Graph.Apps.Diagnostic
{
    [MyTextSurfaceScript(ID, TITLE)]
    public class SessionDebugSurfaceScript : MyTSSCommon
    {
        public const string ID = "SessionDebug";
        public const string TITLE = "LCDMod Session Debug";
        const string DEBUG_FONT = "Monospace";
        const float LINE_SCALE = 0.62f;
        static readonly Color RunningColor = new Color(80, 220, 120);
        static readonly Color IdleColor = new Color(230, 90, 90);
        static readonly Color SleepingColor = new Color(235, 210, 60);

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


            if (Surface.ContentType == ContentType.SCRIPT)
            {
                // force screen refresh
                Surface.ContentType = ContentType.TEXT_AND_IMAGE;
                Surface.ContentType = ContentType.SCRIPT;
            }
            
            var viewBox = GetViewBox();
            var snapshot = LcdModSessionComponent.DebugSnapshot;
            var lines = BuildDebugLines(snapshot);
            var lineHeight = Surface.MeasureStringInPixels(new StringBuilder("A"), DEBUG_FONT, LINE_SCALE).Y + 2f;
            var start = viewBox.Position + new Vector2(8f, 8f);

            using (var frame = Surface.DrawFrame())
            {
                AddBackground(frame, new Color(Surface.BackgroundColor, 0.66f));

                for (int i = 0; i < lines.Count; i++)
                {
                    frame.Add(new MySprite
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
            }
        }

        RectangleF GetViewBox()
        {
            var sizeOffset = (Surface.TextureSize - Surface.SurfaceSize) / 2f;
            var padding = (Surface.TextPadding / 100f) * Surface.SurfaceSize;
            sizeOffset += padding / 2f;
            return new RectangleF(sizeOffset.X, sizeOffset.Y, Surface.SurfaceSize.X - padding.X, Surface.SurfaceSize.Y - padding.Y);
        }

        List<DebugLine> BuildDebugLines(SessionDebugSnapshot snapshot)
        {
            var lines = new List<DebugLine>(32);
            lines.Add(new DebugLine("LCDMod Session Debug", Color.White));
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
