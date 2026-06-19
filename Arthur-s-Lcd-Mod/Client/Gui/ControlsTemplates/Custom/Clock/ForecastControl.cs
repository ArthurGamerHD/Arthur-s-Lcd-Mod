using System;
using System.Collections.Generic;
using LcdMod.Client.Gui.ControlsTemplates.Basic;
using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace LcdMod.Client.Gui.ControlsTemplates.Custom.Clock
{
    internal sealed class ForecastControl : RectangleControl
    {
        readonly SpriteIconControl _icon;
        readonly TextBlock _title;
        readonly TextBlock _arrival;

        public ForecastControl()
            : base(default(RectangleF))
        {
            _icon = new SpriteIconControl
            {
                SpriteName = "WeatherSun",
                Tint = Color.White,
                SizeRatio = 1f
            };
            _title = new TextBlock(default(RectangleF))
            {
                Text = "Clear",
                Ellipsize = true,
                HorizontalAlignment = TextAlignment.CENTER,
                VerticalAlignment = TextBlockVerticalAlignment.Center
            };
            _arrival = new TextBlock(default(RectangleF))
            {
                Ellipsize = true,
                HorizontalAlignment = TextAlignment.CENTER,
                VerticalAlignment = TextBlockVerticalAlignment.Top
            };

            AddChild(_icon);
            AddChild(_title);
            AddChild(_arrival);

            IconHeightRatio = 0.60f;
            TitleHeightRatio = 0.24f;
        }

        public float IconHeightRatio { get; set; }
        public float TitleHeightRatio { get; set; }

        public string SpriteName
        {
            get { return _icon.SpriteName; }
            set { _icon.SpriteName = string.IsNullOrWhiteSpace(value) ? "Warning" : value; }
        }

        public string Title
        {
            get { return _title.Text; }
            set { _title.Text = value ?? string.Empty; }
        }

        public string Arrival
        {
            get { return _arrival.Text; }
            set { _arrival.Text = value ?? string.Empty; }
        }

        public float TitleFontScale
        {
            get { return _title.FontScale; }
            set { _title.FontScale = value; }
        }

        public float ArrivalFontScale
        {
            get { return _arrival.FontScale; }
            set { _arrival.FontScale = value; }
        }

        public override void Arrange(RectangleF bounds)
        {
            base.Arrange(bounds);
            ArrangeChildren();
        }

        protected override void RenderDefault(List<MySprite> sprites)
        {
            ArrangeChildren();
            _icon.Render(sprites);
            _title.Render(sprites);
            _arrival.Render(sprites);
        }

        void ArrangeChildren()
        {
            RectangleF rect = GetViewBox();
            float iconRatio = MathHelper.Clamp(IconHeightRatio, 0.1f, 0.85f);
            float titleRatio = MathHelper.Clamp(TitleHeightRatio, 0.05f, 0.6f);
            float iconHeight = rect.Height * iconRatio;
            float titleHeight = rect.Height * Math.Min(titleRatio, Math.Max(0.05f, 1f - iconRatio));
            float arrivalHeight = Math.Max(0f, rect.Height - iconHeight - titleHeight);

            _icon.Arrange(new RectangleF(rect.X, rect.Y, rect.Width, iconHeight));
            _title.Arrange(new RectangleF(rect.X, rect.Y + iconHeight, rect.Width, titleHeight));
            _arrival.Arrange(new RectangleF(rect.X, rect.Y + iconHeight + titleHeight, rect.Width, arrivalHeight));
        }
    }

}
