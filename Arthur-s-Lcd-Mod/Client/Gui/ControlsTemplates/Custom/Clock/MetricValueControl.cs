using System.Collections.Generic;
using LcdMod.Client.Gui.ControlsTemplates.Basic;
using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace LcdMod.Client.Gui.ControlsTemplates.Custom.Clock
{
    internal sealed class MetricValueControl : RectangleControl
    {
        readonly SpriteIconControl _icon;
        readonly TextBlock _text;

        public MetricValueControl(string iconSpriteName)
            : base(default(RectangleF))
        {
            _icon = new SpriteIconControl
            {
                SpriteName = iconSpriteName,
                SizeRatio = 0.78f
            };
            _text = new TextBlock(default(RectangleF))
            {
                FontScale = 1.1f,
                Ellipsize = true,
                HorizontalAlignment = TextAlignment.LEFT,
                VerticalAlignment = TextBlockVerticalAlignment.Center
            };

            AddChild(_icon);
            AddChild(_text);
        }

        public string Text
        {
            get { return _text.Text; }
            set { _text.Text = value; }
        }

        public string IconSpriteName
        {
            get { return _icon.SpriteName; }
            set { _icon.SpriteName = string.IsNullOrWhiteSpace(value) ? "MissingIcon" : value; }
        }

        public Color? IconTint
        {
            get { return _icon.Tint; }
            set { _icon.Tint = value; }
        }

        public float IconSizeRatio
        {
            get { return _icon.SizeRatio; }
            set { _icon.SizeRatio = value; }
        }

        public new Color? TextColor
        {
            get { return _text.TextColor; }
            set { _text.TextColor = value; }
        }

        public new float FontScale
        {
            get { return _text.FontScale; }
            set { _text.FontScale = value; }
        }

        protected override void RenderDefault(List<MySprite> sprites)
        {
            var rect = GetViewBox();
            if (rect.Width <= 0f || rect.Height <= 0f)
                return;

            float gap = 2f * LayoutScale;
            float iconSize = MathHelper.Min(rect.Height, MathHelper.Max(1f, rect.Width * 0.24f));
            iconSize = MathHelper.Max(1f, iconSize - gap);
            var iconRect = new RectangleF(rect.X, rect.Center.Y - iconSize * 0.5f, iconSize, iconSize);
            var textRect = new RectangleF(
                iconRect.Right + gap,
                rect.Y,
                MathHelper.Max(1f, rect.Right - iconRect.Right - gap),
                rect.Height);

            _icon.Arrange(iconRect);
            _text.Arrange(textRect);
            _icon.Render(sprites);
            _text.Render(sprites);
        }
    }
}