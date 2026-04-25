using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Graph.System;
using Sandbox.Game.Components;
using Sandbox.Game.Entities;
using Sandbox.Game.GameSystems.TextSurfaceScripts;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI;
using VRage.Utils;
using VRageMath;

namespace Graph.Apps
{
    [MyTextSurfaceScript(ID, TITLE)]
    public class SessionDebugSurfaceScript : MyTSSCommon
    {
        public const string ID = "SessionDebug";
        public const string TITLE = "LCDMod Session Debug";
        private const string DEBUG_FONT = "Monospace";
        private const float LINE_SCALE = 0.62f;
        private static readonly Color RunningColor = new Color(80, 220, 120);
        private static readonly Color IdleColor = new Color(230, 90, 90);
        private static readonly Color SleepingColor = new Color(235, 210, 60);

        private ITerminalProperty<float> _screenRotationTerminalProperty;

        private readonly List<MySprite> _sprites = new List<MySprite>();
        private Func<int> _surfaceIndex = () => 0;

        public SessionDebugSurfaceScript(IMyTextSurface surface, IMyCubeBlock block, Vector2 size)
            : base(surface, block, size)
        {
            ResolveSurfaceIndex();
            LcdModSessionComponent.OnAfterSimulationUpdate += HandleAfterSimulationUpdate;
        }

        public override ScriptUpdate NeedsUpdate => ScriptUpdate.Update10;

        public override void Dispose()
        {
            LcdModSessionComponent.OnAfterSimulationUpdate -= HandleAfterSimulationUpdate;
            base.Dispose();
        }

        private void HandleAfterSimulationUpdate()
        {
            if (Surface == null)
                return;

            var ent = (MyCubeBlock)Block;
            var renderComp = (MyRenderComponentScreenAreas)ent.Render;
            var textureSize = (Vector2I)Surface.TextureSize;
            Vector2 aspectRatio;

            var surfaceSize = Surface.SurfaceSize;
            if (surfaceSize.X > surfaceSize.Y)
                aspectRatio = new Vector2(1f, 1f * surfaceSize.Y / surfaceSize.X);
            else
                aspectRatio = new Vector2(1f * surfaceSize.X / surfaceSize.Y, 1f);


            var viewBox = GetViewBox();
            var snapshot = LcdModSessionComponent.DebugSnapshot;
            var lines = BuildDebugLines(snapshot);
            var lineHeight = Surface.MeasureStringInPixels(new StringBuilder("A"), DEBUG_FONT, LINE_SCALE).Y + 2f;
            var start = viewBox.Position + new Vector2(8f, 8f);

            _sprites.Clear();

            for (var i = 0; i < lines.Count; i++)
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

            renderComp.RenderSpritesToTexture(_surfaceIndex(), _sprites, textureSize, aspectRatio,
                Surface.ScriptBackgroundColor, Surface.BackgroundAlpha);
        }

        private void ResolveSurfaceIndex()
        {
            if (Block is IMyTextPanel)
            {
                foreach (var myComponentBase in Block.Components)
                    if (myComponentBase is IMyLcdSurfaceComponent)
                    {
                        var surface = myComponentBase as IMyLcdSurfaceComponent;
                        _surfaceIndex = () => surface.SelectedRotationIndex;
                        return;
                    }

                return;
            }


            var surfaceProvider = Block as IMyTextSurfaceProvider;
            if (surfaceProvider == null)
                return;

            for (var i = 0; i < surfaceProvider.SurfaceCount; i++)
                if (surfaceProvider.GetSurface(i) == Surface)
                {
                    var index = i;
                    _surfaceIndex = () => index;
                    return;
                }

            MyLog.Default.Log(MyLogSeverity.Error, "Failed to find surface for {0}, defaulting to surface 0", Block);
        }

        private int GetRotationIndex(IMyTerminalBlock block)
        {
            _screenRotationTerminalProperty = _screenRotationTerminalProperty ??
                                              block.GetProperty("Rotate") as ITerminalProperty<float>;
            if (_screenRotationTerminalProperty == null)
                return 0;

            var deg = _screenRotationTerminalProperty.GetValue(block); // 0, 90, 180, 270
            var idx = (int)Math.Round(deg / 90f);
            return (idx % 4 + 4) % 4;
        }

        private RectangleF GetViewBox()
        {
            var sizeOffset = (Surface.TextureSize - Surface.SurfaceSize) / 2f;
            var padding = Surface.TextPadding / 100f * Surface.SurfaceSize;
            sizeOffset += padding / 2f;
            return new RectangleF(sizeOffset.X, sizeOffset.Y, Surface.SurfaceSize.X - padding.X,
                Surface.SurfaceSize.Y - padding.Y);
        }

        private List<DebugLine> BuildDebugLines(SessionDebugSnapshot snapshot)
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

            var shown = 0;
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
                    logic.IsSleeping ? SleepingColor : logic.IsRefreshRunning ? RunningColor : IdleColor));

                shown++;
            }

            return lines;
        }

        private static string ClampToWidth(string value, int width)
        {
            if (string.IsNullOrEmpty(value))
                return new string(' ', width);
            if (value.Length > width)
                return value.Substring(0, width);
            return value.PadRight(width);
        }

        private static string Fixed4(int value)
        {
            return ClampToWidth(value.ToString(), 4);
        }

        private struct DebugLine
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