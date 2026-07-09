#if EXPERIMENTAL
using System;
using System.Collections.Generic;
using LcdMod.Client.Extensions;
using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace LcdMod.Client.Gui.ControlsTemplates.Progress
{
    public sealed class AudioVisualizerModel : ControlModelBase
    {
        public AudioVisualizerModel()
        {
            BarCount = 32;
            CenterLineColor = Color.Black;
            BarSaturation = 1f;
            BarValue = 1f;
            BarAlpha = 1f;
        }

        public int BarCount { get; set; }
        public float[] BarLevels { get; set; }
        public Color CenterLineColor { get; set; }
        public Color? BackgroundColor { get; set; }
        public float BarSaturation { get; set; }
        public float BarValue { get; set; }
        public float BarAlpha { get; set; }
    }

    /// <summary>
    /// Parameterized audio visualizer. Bars are ordered horizontally by frequency,
    /// with even indexes above the center line and odd indexes below it.
    /// </summary>
    public sealed class AudioVisualizer : RectangleControl
    {
        const float DEFAULT_BAR_WIDTH_RATIO = .64f;
        const float CENTER_LINE_HEIGHT = 2f;

        public AudioVisualizer(RectangleF bounds, AudioVisualizerModel model)
            : base(bounds, CursorType.Default, model ?? new AudioVisualizerModel())
        {
        }

        AudioVisualizerModel VisualizerModel
        {
            get { return DataContext as AudioVisualizerModel; }
        }

        protected override void RenderDefault(List<MySprite> sprites)
        {
            var model = VisualizerModel;
            var rect = GetViewBox();
            if (rect.Width <= 0f || rect.Height <= 0f)
                return;

            if (model != null && model.BackgroundColor.HasValue)
            {
                sprites.Add(new MySprite
                {
                    Type = SpriteType.TEXTURE,
                    Data = "SquareSimple",
                    Position = new Vector2(rect.X, rect.Center.Y),
                    Size = rect.Size,
                    Color = model.BackgroundColor.Value,
                    Alignment = TextAlignment.LEFT
                });
            }

            int barCount = model == null ? 32 : model.BarCount;
            if (barCount <= 0)
                barCount = 32;

            float layoutScale = LayoutScale <= 0f ? 1f : LayoutScale;
            float maxLineHeight = Math.Max(1f, 3f * layoutScale);
            float lineHeight = MathHelper.Clamp(CENTER_LINE_HEIGHT * layoutScale, 1f, maxLineHeight);
            float centerY = rect.Center.Y;
            float availableHalfHeight = Math.Max(1f, (rect.Height - lineHeight) * .5f);
            int columnCount = Math.Max(1, (barCount + 1) / 2);
            float step = rect.Width / columnCount;
            float minimumBarWidth = Math.Max(1f, 2f * layoutScale);
            float barWidth = Math.Max(minimumBarWidth, step * DEFAULT_BAR_WIDTH_RATIO);
            if (barWidth > step)
                barWidth = Math.Max(1f, step);
            float saturation = MathHelper.Clamp(model == null ? 1f : model.BarSaturation, 0f, 1f);
            float value = MathHelper.Clamp(model == null ? 1f : model.BarValue, 0f, 1f);
            float alpha = MathHelper.Clamp(model == null ? 1f : model.BarAlpha, 0f, 1f);
            float[] levels = model == null ? null : model.BarLevels;

            for (int i = 0; i < barCount; i++)
            {
                float level = 0f;
                if (levels != null && i < levels.Length)
                    level = MathHelper.Clamp(levels[i], 0f, 1f);

                float barHeight = Math.Max(0f, level * availableHalfHeight);
                if (barHeight <= .25f)
                    continue;

                int column = i / 2;
                float x = rect.X + step * column + (step - barWidth) * .5f;
                bool top = (i & 1) == 0;
                float y = top
                    ? centerY - lineHeight * .5f - barHeight * .5f
                    : centerY + lineHeight * .5f + barHeight * .5f;

                float hue = columnCount <= 1 ? 0f : column / (float)(columnCount - 1);
                var color = new Vector3(hue, saturation, value).HSVtoColor();
                color = new Color(color, alpha);

                sprites.Add(new MySprite
                {
                    Type = SpriteType.TEXTURE,
                    Data = "SquareSimple",
                    Position = new Vector2(x, y),
                    Size = new Vector2(barWidth, barHeight),
                    Color = color,
                    Alignment = TextAlignment.LEFT
                });
            }
        }
    }
}
#endif
