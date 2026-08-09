using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace LcdMod.Client.Gui.ControlsTemplates.Progress
{
    public static class DonutDualPanel
    {
        public static void CreateSprites(
            List<MySprite> sprites,
            string title,
            IMyTextSurface surface,
            Vector2 margin,
            Vector2 size,
            float value,
            float value2,
            Color? color = null,
            bool turnDarkOnComplete = false,
            bool showTitle = true)
        {
            if (color == null)
                color = surface.ScriptForegroundColor;

            var origo = new Vector2(margin.X, 512 - margin.Y);
            var backgroundColor = surface.ScriptForegroundColor;

            if (showTitle) DonutPanel.DrawTitle(sprites, title, origo, size, value, color.Value);
            value = MathHelper.Clamp(value, 0f, 1f);
            value2 = MathHelper.Clamp(value2, 0f, 1f);

            DonutSegment[] segments;
            if (value <= value2)
            {
                segments = new[]
                {
                    new DonutSegment(value, backgroundColor),
                    new DonutSegment(value2 - value, color.Value)
                };
            }
            else
            {
                segments = new[]
                {
                    new DonutSegment(value2, color.Value),
                    new DonutSegment(value - value2, backgroundColor)
                };
            }

            float outerRadius = Math.Max(0f, Math.Min(size.X, size.Y) * 0.5f);
            DonutPanel.DrawSegmentedDonut(
                sprites,
                origo,
                outerRadius * 0.68f,
                outerRadius,
                segments,
                DonutPanel.GetDarkenedColor(backgroundColor),
                gapPixels: 2f,
                gapColor: surface.ScriptBackgroundColor);
        }
    }
}
