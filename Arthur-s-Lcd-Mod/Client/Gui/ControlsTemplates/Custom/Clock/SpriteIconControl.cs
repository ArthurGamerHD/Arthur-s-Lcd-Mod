using System.Collections.Generic;
using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace LcdMod.Client.Gui.ControlsTemplates.Custom.Clock
{
    internal sealed class SpriteIconControl : RectangleControl
    {
        public SpriteIconControl()
            : base(default(RectangleF))
        {
            SpriteName = "MissingIcon";
            SizeRatio = 0.8f;
        }

        public string SpriteName { get; set; }
        public Color? Tint { get; set; }
        public float SizeRatio { get; set; }

        protected override void RenderDefault(List<MySprite> sprites)
        {
            var rect = GetViewBox();
            if (rect.Width <= 0f || rect.Height <= 0f)
                return;

            float size = MathHelper.Max(1f, MathHelper.Min(rect.Width, rect.Height) *
                                            MathHelper.Clamp(SizeRatio, 0.1f, 1f));
            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXTURE,
                Data = string.IsNullOrWhiteSpace(SpriteName) ? "MissingIcon" : SpriteName,
                Position = rect.Center,
                Size = new Vector2(size),
                Color = Tint ?? TextColor,
                Alignment = TextAlignment.CENTER
            });
        }
    }
}