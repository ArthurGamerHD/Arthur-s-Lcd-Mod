using System;
using System.Collections.Generic;
using Generated;
using Graph.Apps.Abstract;
using Graph.Helpers;
using Graph.Panels;
using Graph.System.TerminalControls.Color;
using Graph.System.TerminalControls.Generic;
using Graph.System.TerminalControls.Groups;
using Sandbox.Game.GameSystems.TextSurfaceScripts;
using Sandbox.ModAPI;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI;
using VRage.Utils;
using VRageMath;

namespace Graph.Apps
{
    [MyTextSurfaceScript(ID, TITLE)]
    public partial class ThrustSurfaceScript : SurfaceScriptBase,
        IUsesTerminalControlGroup<ColorsTerminalControlGroup>,
        IUsesTerminalControl<SwitchToggleHeader>,
        IUsesTerminalControl<SliderScale>
    {
        public const string ID    = "LCDMod_Thrust";
        public const string TITLE = "HelpScreen_JoystickThrust";
        
        protected override string DefaultTitle => TITLE;

        static readonly string[] DirectionLabels =
        {
            "Forward",
            "Backward",
            "Left",
            "Right",
            "Up",
            "Down",
        };

        public ThrustSurfaceScript(IMyTextSurface surface, IMyCubeBlock block, Vector2 size)
            : base(surface, block, size)
        {

        }

        public override void Run()
        {
            base.Run();
            if (Config == null) return;

            var maxThrust = new double[6];
            var curThrust = new double[6];
            bool hasAny   = false;
            
            try
            {
                var grid = Block?.CubeGrid as IMyCubeGrid;
                if (grid != null)
                {
                    var slims = new List<IMySlimBlock>();
                    grid.GetBlocks(slims, b => b.FatBlock is IMyThrust);

                    for (int i = 0; i < slims.Count; i++)
                    {
                        var thr = slims[i].FatBlock as IMyThrust;
                        if (thr == null) continue;

                        // A thruster pushes the ship OPPOSITE to the direction its front face points.
                        var pushDir = Base6Directions.GetOppositeDirection(thr.Orientation.Forward);
                        int idx = DirIndex(pushDir);
                        if (idx < 0) continue;

                        double max = 0, cur = 0;
                        try { max = thr.MaxThrust; }     catch { }
                        try { cur = thr.CurrentThrust; } catch { }

                        maxThrust[idx] += max;
                        curThrust[idx] += cur;
                        if (max > 0) hasAny = true;
                    }
                }
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLine($"[LCDMod] ThrustGraph error: {ex.Message}");
            }
            
            if (!hasAny)
            {
                Empty();
                return;
            }
            
            using (var frame = Surface.DrawFrame())
            {
                var sprites = new List<MySprite>();
                AddBackground(sprites);
                DrawTitle(sprites);
                DrawBars(sprites, maxThrust, curThrust);
                frame.AddRange(sprites);
            }
        }

        void DrawBars(List<MySprite> sprites, double[] maxThrust, double[] curThrust)
        {
            int activeCount = 0;
            for (int d = 0; d < 6; d++)
                if (maxThrust[d] > 0) activeCount++;
            if (activeCount == 0) return;

            float margin  = ViewBox.Width * Margin;
            float availH  = ViewBox.Height - (CaretY - ViewBox.Y);
            float rowH    = Math.Max(20f * Scale, availH / activeCount);
            float barH    = Math.Min(16f * Scale, rowH * 0.50f);
            float labelW  = 88f  * Scale;
            float valueW  = 100f * Scale;
            float barW    = Math.Max(20f * Scale, ViewBox.Width - margin * 2f - labelW - valueW);

            int brightness = Surface.ScriptForegroundColor.R
                           + Surface.ScriptForegroundColor.G
                           + Surface.ScriptForegroundColor.B;
            Color trackColor = brightness > 96 ? Color.Black : Color.Gray;

            float y = CaretY;

            for (int d = 0; d < 6; d++)
            {
                if (maxThrust[d] <= 0) continue;

                float fill  = (float)Math.Max(0.0, Math.Min(1.0, curThrust[d] / maxThrust[d]));
                float x     = ViewBox.Position.X + margin;
                float textY = y + (rowH - 18f * Scale) * 0.5f; // vertically center text in row

                sprites.Add(Text(DirectionLabels[d], new Vector2(x, textY), 0.8f * Scale));
                x += labelW;

                float barY = y + (rowH - barH) * 0.5f;
                var bar = new BarPanel(
                    new Vector2(x, barY),
                    new Vector2(barW, barH),
                    Config.HeaderColor,
                    trackColor
                );
                sprites.AddRange(bar.GetSprites(fill));
                x += barW + 6f * Scale;

                sprites.Add(Text(FormatingHelper.NewtonForceToString(maxThrust[d]), new Vector2(x, textY), 0.8f * Scale));

                y += rowH;
            }
        }

        static int DirIndex(Base6Directions.Direction dir)
        {
            switch (dir)
            {
                case Base6Directions.Direction.Forward:  return 0;
                case Base6Directions.Direction.Backward: return 1;
                case Base6Directions.Direction.Left:     return 2;
                case Base6Directions.Direction.Right:    return 3;
                case Base6Directions.Direction.Up:       return 4;
                case Base6Directions.Direction.Down:     return 5;
                default:                                 return -1;
            }
        }
    }
}
