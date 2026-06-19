using System.Collections.Generic;
using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace LcdMod.Client.Gui.ControlsTemplates.Custom.Clock
{
    internal sealed class WeatherIconControl : RectangleControl
    {
        public WeatherIconControl()
            : base(default(RectangleF))
        {
            BaseSpriteName = "MissingIcon";
            EffectSpriteName = "MissingIcon";
            Tint = Color.White;
            SizeRatio = 0.86f;
        }

        public string BaseSpriteName { get; set; }
        public string EffectSpriteName { get; set; }
        public bool ShowEffect { get; set; }
        public Color Tint { get; set; }
        public float SizeRatio { get; set; }

        protected override void RenderDefault(List<MySprite> sprites)
        {
            var rect = GetViewBox();
            if (rect.Width <= 0f || rect.Height <= 0f)
                return;

            float size = MathHelper.Max(1f, MathHelper.Min(rect.Width, rect.Height) * MathHelper.Clamp(SizeRatio, 0.1f, 1f));
            if (!ShowEffect)
            {
                AddIcon(sprites, BaseSpriteName, rect.Center, size);
                return;
            }

            AddIcon(sprites, EffectSpriteName, rect.Center, size);
        }

        void AddIcon(List<MySprite> sprites, string spriteName, Vector2 center, float size)
        {
            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXTURE,
                Data = string.IsNullOrWhiteSpace(spriteName) ? "MissingIcon" : spriteName,
                Position = center,
                Size = new Vector2(size),
                Color = Tint,
                Alignment = TextAlignment.CENTER
            });
        }
    }
}