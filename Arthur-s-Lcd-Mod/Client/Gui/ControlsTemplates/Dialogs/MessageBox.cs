using System;
using System.Collections.Generic;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.Gui.ControlsTemplates.Basic;
using LcdMod.Client.Gui.ControlsTemplates.Panels;
using LcdMod.Client.Gui.Styling;
using LcdMod.Client.Helpers;
using VRage.Game.GUI.TextPanel;
using VRageMath;
using static LcdMod.Common.Helpers.Constants;
using InteractiveSurfaceScript = LcdMod.Client.SurfaceScripts.Abstract.InteractiveSurfaceScript;

namespace LcdMod.Client.Gui.ControlsTemplates.Dialogs
{
    sealed class MessageBox : Dialog
    {
        Panel _rootControl;
        RectangleControl _overlayControl;
        RectangleControl _shadowControl;
        RectangleControl _cardBackgroundControl;
        MessageBoxContentPanel _contentPanel;
        TextBlock _titleShadowControl;
        TextBlock _titleControl;
        MessageBoxIconControl _iconControl;
        TextBlock _contentControl;
        Button _button1Control;
        Button _button2Control;
        Action<object, object> _button1Callback;
        Action<object, object> _button2Callback;

        string _title;
        string _content;
        string _button1;
        string _button2;
        string _icon;

        public MessageBox(IApp parentApp)
            : base(parentApp)
        {
        }

        public void Show(
            string title,
            string content,
            string button1,
            string button2,
            Action<object, object> button1Callback,
            Action<object, object> button2Callback,
            string icon)
        {
            _title = title ?? string.Empty;
            _content = content ?? string.Empty;
            _button1 = string.IsNullOrEmpty(button1) ? LocHelper.GetLoc(MOD_PREFIX + "Common_Button_OK") : button1;
            _button2 = button2 ?? string.Empty;
            _icon = icon;

            _button1Callback = button1Callback;
            _button2Callback = button2Callback;
            MarkDirty();
        }

        protected override void BuildDialogControls(
            InteractiveSurfaceScript owner,
            RectangleF viewBox,
            float scale,
            float fontScale,
            Sandbox.ModAPI.Ingame.IMyTextSurface surface,
            Color textColor,
            Color backgroundColor,
            Color panelColor,
            Vector2 cursorPosition)
        {
            EnsureContainer(viewBox);
            EnsureTree();
            ContainerControl.AddChild(_rootControl);
            _rootControl.SetVisible(true);

            var cardColor = ResolveColor(ThemeResources.SurfaceContainerHighColor);
            var cardTextColor = ResolveColor(ThemeResources.OnSurfaceColor);
            var shadowColor = ResolveColor(ThemeResources.ShadowColor);

            var layout = MeasureLayout(viewBox, scale, fontScale, surface);
            RegisterDialogCard(layout.CardRect);

            ConfigureSurfaceControls(viewBox, layout.CardRect, cardColor, shadowColor);
            ConfigureTextControls(cardTextColor, shadowColor);
            ConfigureButton(_button1Control, _button1);
            ConfigureButton(_button2Control, _button2);
            _button2Control.SetVisible(layout.ShowButton2);
            _button2Control.SetEnabled(layout.ShowButton2);
            _button2Control.SetCursor(layout.ShowButton2 ? CursorType.Hand : CursorType.Default);

            _contentPanel.Configure(layout);
            _rootControl.SetRect(viewBox);
            _rootControl.Render(Sprites);
        }

        void EnsureTree()
        {
            if (_rootControl != null)
                return;

            _rootControl = new Panel(default(RectangleF));
            _overlayControl = new RectangleControl(default(RectangleF));
            _shadowControl = new RectangleControl(default(RectangleF));
            _cardBackgroundControl = new RectangleControl(default(RectangleF));
            _titleShadowControl = CreateTitleTextBlock();
            _titleControl = CreateTitleTextBlock();
            _iconControl = new MessageBoxIconControl();
            _contentControl = new TextBlock(default(RectangleF))
            {
                Ellipsize = true,
                FontScale = 0.58f,
                HorizontalAlignment = TextAlignment.LEFT,
                LineSpacingPixels = 2f,
                VerticalAlignment = TextBlockVerticalAlignment.Top,
                Wrapping = TextBlockWrapping.Wrap
            };
            _button1Control = new Button(
                default(RectangleF),
                new ButtonModel
                {
                    Text = _button1,
                    Clicked = OnButton1Click
                });
            _button2Control = new Button(
                default(RectangleF),
                new ButtonModel
                {
                    Text = _button2,
                    Clicked = OnButton2Click
                });
            _contentPanel = new MessageBoxContentPanel(
                _titleShadowControl,
                _titleControl,
                _iconControl,
                _contentControl,
                _button1Control,
                _button2Control);

            _rootControl.AddChild(_overlayControl);
            _rootControl.AddChild(_shadowControl);
            _rootControl.AddChild(_cardBackgroundControl);
            _rootControl.AddChild(_contentPanel);
        }

        static TextBlock CreateTitleTextBlock()
        {
            return new TextBlock(default(RectangleF))
            {
                Ellipsize = true,
                FontScale = 0.82f,
                HorizontalAlignment = TextAlignment.CENTER,
                VerticalAlignment = TextBlockVerticalAlignment.Center,
                Wrapping = TextBlockWrapping.NoWrap
            };
        }

        MessageBoxLayout MeasureLayout(
            RectangleF viewBox,
            float scale,
            float fontScale,
            Sandbox.ModAPI.Ingame.IMyTextSurface surface)
        {
            var titleScale = 0.82f * scale * fontScale;
            var contentScale = 0.58f * scale * fontScale;
            var buttonScale = 0.58f * scale * fontScale;
            var padding = new Vector2(18f, 14f) * scale;
            var spacing = 10f * scale;
            var buttonSpacing = 10f * scale;
            var buttonHeight = Math.Max(24f * scale, MeasureLineHeight(buttonScale, surface) + 10f * scale);
            var minButtonWidth = 78f * scale;

            var titleSize = MeasureText(_title, titleScale, surface);
            var closeSize = GetDialogCloseButtonSize(scale);
            var headerHeight = Math.Max(titleSize.Y, closeSize.Y);
            var contentLines = SplitLines(_content);
            var lineStep = MeasureLineHeight(contentScale, surface) + 2f * scale;
            var maxContentWidth = MeasureWidestLine(contentLines, contentScale, surface);

            var hasIcon = !string.IsNullOrEmpty(_icon);
            var iconSize = hasIcon ? Math.Max(32f * scale, lineStep * Math.Min(2.5f, Math.Max(1f, contentLines.Length))) : 0f;
            var iconGap = hasIcon ? 12f * scale : 0f;
            var naturalContentBlockWidth = maxContentWidth + iconSize + iconGap;

            var button1Size = MeasureText(_button1, buttonScale, surface);
            var button2Size = MeasureText(_button2, buttonScale, surface);
            var button1Width = Math.Max(minButtonWidth, button1Size.X + 28f * scale);
            var showButton2 = _button2Callback != null || !string.IsNullOrWhiteSpace(_button2);
            var button2Width = showButton2 ? Math.Max(minButtonWidth, button2Size.X + 28f * scale) : 0f;
            var buttonsWidth = showButton2 ? button1Width + buttonSpacing + button2Width : button1Width;

            var maxCardWidth = Math.Max(1f, viewBox.Width - padding.X * 2f);
            var cardWidth = Math.Max(240f * scale,
                Math.Max(titleSize.X, Math.Max(naturalContentBlockWidth, buttonsWidth)) + padding.X * 2f);
            cardWidth = Math.Min(cardWidth, maxCardWidth);

            var contentAreaWidth = Math.Max(0f, cardWidth - padding.X * 2f);
            var maxTextWidth = Math.Max(0f, contentAreaWidth - iconSize - iconGap);
            var contentTextWidth = maxContentWidth <= 0f ? 0f : Math.Min(maxContentWidth, maxTextWidth);
            if (maxContentWidth > 0f && contentTextWidth < 1f)
                contentTextWidth = 1f;

            var contentLineCount = CountWrappedContentLines(contentLines, contentTextWidth, contentScale, 2f * scale, surface);
            var contentTextHeight = Math.Max(lineStep, lineStep * contentLineCount);
            var contentBlockWidth = contentTextWidth + iconSize + iconGap;
            var desiredContentBlockHeight = Math.Max(contentTextHeight, iconSize);
            var maxCardHeight = Math.Max(1f, viewBox.Height - padding.Y * 2f);
            var desiredCardHeight = padding.Y * 2f + headerHeight + spacing + desiredContentBlockHeight + spacing + buttonHeight;
            var cardHeight = Math.Min(desiredCardHeight, maxCardHeight);
            var maxContentBlockHeight = Math.Max(1f,
                cardHeight - padding.Y * 2f - headerHeight - spacing - spacing - buttonHeight);
            var contentBlockHeight = Math.Min(desiredContentBlockHeight, maxContentBlockHeight);
            cardHeight = padding.Y * 2f + headerHeight + spacing + contentBlockHeight + spacing + buttonHeight;

            var cardRect = new RectangleF(
                viewBox.Center.X - cardWidth * 0.5f,
                viewBox.Center.Y - cardHeight * 0.5f,
                cardWidth,
                cardHeight);

            return new MessageBoxLayout
            {
                Button1Width = button1Width,
                Button2Width = button2Width,
                ButtonHeight = buttonHeight,
                ButtonSpacing = buttonSpacing,
                CardRect = cardRect,
                ContentBlockHeight = contentBlockHeight,
                ContentBlockWidth = contentBlockWidth,
                ContentTextWidth = contentTextWidth,
                HasIcon = hasIcon,
                HeaderHeight = headerHeight,
                IconSize = iconSize,
                IconGap = iconGap,
                Padding = padding,
                ShadowOffset = 2f * titleScale,
                ShowButton2 = showButton2,
                Spacing = spacing
            };
        }

        void ConfigureSurfaceControls(RectangleF viewBox, RectangleF cardRect, Color cardColor, Color shadowColor)
        {
            _overlayControl.SetRect(viewBox);
            _overlayControl.BackgroundColor = new Color(0, 0, 0, 128);
            _overlayControl.BorderRadiusPixels = 0f;
            _overlayControl.BorderThicknessPixels = 0f;
            _overlayControl.SetVisible(true);

            _shadowControl.SetRect(new RectangleF(cardRect.Position + 2f, cardRect.Size));
            _shadowControl.BackgroundColor = shadowColor;
            _shadowControl.BorderRadiusPixels = BorderRenderer.DEFAULT_RADIUS_PIXELS;
            _shadowControl.BorderThicknessPixels = 0f;
            _shadowControl.SetVisible(true);

            _cardBackgroundControl.SetRect(cardRect);
            _cardBackgroundControl.BackgroundColor = cardColor;
            _cardBackgroundControl.BorderRadiusPixels = BorderRenderer.DEFAULT_RADIUS_PIXELS;
            _cardBackgroundControl.BorderThicknessPixels = 0f;
            _cardBackgroundControl.SetVisible(true);
        }

        void ConfigureTextControls(Color cardTextColor, Color shadowColor)
        {
            _titleShadowControl.Text = _title;
            _titleShadowControl.FontId = TextFont;
            _titleShadowControl.TextColor = shadowColor;
            _titleShadowControl.SetVisible(!string.IsNullOrEmpty(_title));

            _titleControl.Text = _title;
            _titleControl.FontId = TextFont;
            _titleControl.TextColor = cardTextColor;
            _titleControl.SetVisible(true);

            _contentControl.Text = _content;
            _contentControl.FontId = TextFont;
            _contentControl.TextColor = cardTextColor;
            _contentControl.SetVisible(true);

            _iconControl.SpriteName = _icon;
            _iconControl.Tint = cardTextColor;
            _iconControl.SetVisible(!string.IsNullOrEmpty(_icon));
        }

        float MeasureWidestLine(string[] lines, float textScale, Sandbox.ModAPI.Ingame.IMyTextSurface surface)
        {
            var maxWidth = 0f;
            for (var i = 0; i < lines.Length; i++)
            {
                var size = MeasureText(lines[i], textScale, surface);
                if (size.X > maxWidth)
                    maxWidth = size.X;
            }

            return maxWidth;
        }

        int CountWrappedContentLines(
            string[] lines,
            float maxWidth,
            float textScale,
            float lineSpacing,
            Sandbox.ModAPI.Ingame.IMyTextSurface surface)
        {
            if (lines == null || lines.Length == 0)
                return 1;

            var lineCount = 0;
            var fontId = TextFont;
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                if (string.IsNullOrEmpty(line) || maxWidth <= 1f)
                {
                    lineCount++;
                    continue;
                }

                if (MeasureText(line, textScale, surface).X <= maxWidth)
                {
                    lineCount++;
                    continue;
                }

                var wrapped = TextWrappingHelper.WrapText(
                    line,
                    surface,
                    fontId,
                    textScale,
                    maxWidth,
                    10000f,
                    lineSpacing,
                    false);
                lineCount += Math.Max(1, wrapped == null ? 0 : wrapped.Count);
            }

            return Math.Max(1, lineCount);
        }

        static string[] SplitLines(string content)
        {
            if (string.IsNullOrEmpty(content))
                return new[] { string.Empty };

            return content.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        }

        void OnButton1Click(ButtonModel model, object sender)
        {
            var callback = _button1Callback;
            Dismiss();

            if (callback != null)
                callback(model, sender);
        }

        void OnButton2Click(ButtonModel model, object sender)
        {
            var callback = _button2Callback;
            Dismiss();

            if (callback != null)
                callback(model, sender);
        }

        protected override void OnDismiss()
        {
            _button1Callback = null;
            _button2Callback = null;

            if (_rootControl != null)
                _rootControl.SetVisible(false);
            if (_button1Control != null)
                _button1Control.SetVisible(false);
            if (_button2Control != null)
                _button2Control.SetVisible(false);
        }

        static void ConfigureButton(Button button, string text)
        {
            if (button == null)
                return;

            var model = button.DataContext as ButtonModel;
            if (model != null)
            {
                model.Text = text ?? string.Empty;
                model.Enabled = true;
            }

            button.SetEnabled(true);
            button.SetVisible(true);
            button.SetStyleId("Primary");
            button.SetClass("ControlBase Button");
            button.SetCursor(CursorType.Hand);
            button.CustomRender = null;
        }

        sealed class MessageBoxLayout
        {
            public RectangleF CardRect;
            public Vector2 Padding;
            public float HeaderHeight;
            public float Spacing;
            public float ContentBlockWidth;
            public float ContentBlockHeight;
            public float ContentTextWidth;
            public bool HasIcon;
            public float IconSize;
            public float IconGap;
            public float Button1Width;
            public float Button2Width;
            public float ButtonHeight;
            public float ButtonSpacing;
            public bool ShowButton2;
            public float ShadowOffset;
        }

        sealed class MessageBoxContentPanel : Panel
        {
            readonly TextBlock _titleShadow;
            readonly TextBlock _title;
            readonly MessageBoxIconControl _icon;
            readonly TextBlock _content;
            readonly Button _button1;
            readonly Button _button2;
            MessageBoxLayout _layout;

            public MessageBoxContentPanel(
                TextBlock titleShadow,
                TextBlock title,
                MessageBoxIconControl icon,
                TextBlock content,
                Button button1,
                Button button2)
                : base(default(RectangleF))
            {
                _titleShadow = titleShadow;
                _title = title;
                _icon = icon;
                _content = content;
                _button1 = button1;
                _button2 = button2;

                AddChild(_titleShadow);
                AddChild(_title);
                AddChild(_icon);
                AddChild(_content);
                AddChild(_button1);
                AddChild(_button2);
            }

            public void Configure(MessageBoxLayout layout)
            {
                _layout = layout;
                SetRect(layout == null ? default(RectangleF) : layout.CardRect);
            }

            protected override void ArrangeChildren()
            {
                var layout = _layout;
                if (layout == null)
                    return;

                var rect = Bounds;
                var contentWidth = Math.Max(1f, rect.Width - layout.Padding.X * 2f);
                var currentY = rect.Y + layout.Padding.Y;
                var titleRect = new RectangleF(
                    rect.X + layout.Padding.X,
                    currentY,
                    contentWidth,
                    layout.HeaderHeight);

                _titleShadow.Arrange(new RectangleF(
                    titleRect.X + layout.ShadowOffset,
                    titleRect.Y + layout.ShadowOffset,
                    titleRect.Width,
                    titleRect.Height));
                _title.Arrange(titleRect);

                currentY += layout.HeaderHeight + layout.Spacing;
                var contentStartX = rect.X + layout.Padding.X +
                                    Math.Max(0f, (contentWidth - layout.ContentBlockWidth) * 0.5f);
                var iconSize = layout.HasIcon ? Math.Min(layout.IconSize, layout.ContentBlockHeight) : 0f;
                if (layout.HasIcon && iconSize > 0f)
                {
                    _icon.Arrange(new RectangleF(
                        contentStartX,
                        currentY + (layout.ContentBlockHeight - iconSize) * 0.5f,
                        iconSize,
                        iconSize));
                }

                var textX = contentStartX + (layout.HasIcon ? iconSize + layout.IconGap : 0f);
                _content.Arrange(new RectangleF(
                    textX,
                    currentY,
                    Math.Max(1f, layout.ContentTextWidth),
                    Math.Max(1f, layout.ContentBlockHeight)));

                currentY += layout.ContentBlockHeight + layout.Spacing;
                var buttonsWidth = layout.ShowButton2
                    ? layout.Button1Width + layout.ButtonSpacing + layout.Button2Width
                    : layout.Button1Width;
                var buttonsStartX = rect.Center.X - buttonsWidth * 0.5f;
                _button1.Arrange(new RectangleF(buttonsStartX, currentY, layout.Button1Width, layout.ButtonHeight));
                _button2.Arrange(layout.ShowButton2
                    ? new RectangleF(buttonsStartX + layout.Button1Width + layout.ButtonSpacing, currentY, layout.Button2Width, layout.ButtonHeight)
                    : default(RectangleF));
            }
        }

        sealed class MessageBoxIconControl : RectangleControl
        {
            public MessageBoxIconControl()
                : base(default(RectangleF))
            {
                SpriteName = "MissingIcon";
            }

            public string SpriteName { get; set; }
            public Color Tint { get; set; }

            protected override void RenderDefault(List<MySprite> sprites)
            {
                var rect = GetViewBox();
                if (rect.Width <= 0f || rect.Height <= 0f)
                    return;

                sprites.Add(new MySprite
                {
                    Type = SpriteType.TEXTURE,
                    Data = string.IsNullOrEmpty(SpriteName) ? "MissingIcon" : SpriteName,
                    Position = rect.Center,
                    Size = new Vector2(Math.Min(rect.Width, rect.Height)),
                    Color = Tint,
                    Alignment = TextAlignment.CENTER
                });
            }
        }
    }
}
