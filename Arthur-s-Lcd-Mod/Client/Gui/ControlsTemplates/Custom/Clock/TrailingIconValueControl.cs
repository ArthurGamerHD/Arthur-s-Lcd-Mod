using System.Collections.Generic;
using LcdMod.Client.Gui.ControlsTemplates.Basic;
using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace LcdMod.Client.Gui.ControlsTemplates.Custom.Clock
{
    internal sealed class TrailingIconValueControl : RectangleControl
    {
        readonly TextBlock _text;
        readonly SpriteIconControl _icon;

        public TrailingIconValueControl(string iconSpriteName)
            : base(default(RectangleF))
        {
            _text = new TextBlock(default(RectangleF))
            {
                FontScale = 0.9f,
                Ellipsize = true,
                HorizontalAlignment = TextAlignment.RIGHT,
                VerticalAlignment = TextBlockVerticalAlignment.Center
            };
            _icon = new SpriteIconControl
            {
                SpriteName = iconSpriteName,
                SizeRatio = 0.76f
            };

            AddChild(_text);
            AddChild(_icon);
        }

        public string Text
        {
            get { return _text.Text; }
            set { _text.Text = value ?? string.Empty; }
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

        public float TextScale
        {
            get { return _text.FontScale; }
            set { _text.FontScale = value; }
        }

        public override void Arrange(RectangleF bounds)
        {
            base.Arrange(bounds);
            ArrangeChildren();
        }

        protected override void RenderDefault(List<MySprite> sprites)
        {
            ArrangeChildren();
            _text.Render(sprites);
            _icon.Render(sprites);
        }

        void ArrangeChildren()
        {
            RectangleF rect = GetViewBox();
            if (rect.Width <= 0f || rect.Height <= 0f)
                return;

            float gap = 2f * LayoutScale;
            float iconSize = MathHelper.Min(
                rect.Height,
                MathHelper.Max(1f, rect.Width * 0.34f));
            iconSize = MathHelper.Max(1f, iconSize - gap);

            var iconRect = new RectangleF(
                rect.Right - iconSize,
                rect.Center.Y - iconSize * 0.5f,
                iconSize,
                iconSize);
            var textRect = new RectangleF(
                rect.X,
                rect.Y,
                MathHelper.Max(1f, iconRect.X - rect.X - gap),
                rect.Height);

            _text.Arrange(textRect);
            _icon.Arrange(iconRect);
        }
    }
}