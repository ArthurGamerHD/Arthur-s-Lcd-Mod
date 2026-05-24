using System;
using System.Collections.Generic;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.Extensions;
using LcdMod.Client.Gui.ControlsTemplates.Panels;
using LcdMod.Client.Gui.Tooltip;
using LcdMod.Client.Helpers;
using VRage.Game.GUI.TextPanel;
using VRageMath;
using InteractiveSurfaceScript = LcdMod.Client.SurfaceScripts.Abstract.InteractiveSurfaceScript;

namespace LcdMod.Client.Gui.ControlsTemplates.Interactive
{
    sealed class MessageBox
    {
        readonly IApp _parentApp;
        readonly object _button1Context = new object();
        readonly object _button2Context = new object();
        readonly List<MySprite> _sprites = new List<MySprite>();

        MessageBoxContainerControl _containerControl;
        RectangleControl _button1Control;
        RectangleControl _button2Control;
        Action<object, object> _button1Callback;
        Action<object, object> _button2Callback;

        public bool Dismissed;
        string _title;
        string _content;
        string _button1;
        string _button2;
        string _icon;

        public MessageBox(IApp parentApp)
        {
            if (parentApp == null)
                throw new ArgumentNullException("parentApp");

            _parentApp = parentApp;
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
            _button1 = string.IsNullOrEmpty(button1) ? "OK" : button1;
            _button2 = button2 ?? string.Empty;
            _icon = icon;

            _button1Callback = button1Callback;
            _button2Callback = button2Callback;
        }

        public void AddInteractiveEntries(List<ControlBase> entries)
        {
            if (Dismissed || entries == null)
                return;

            if (_containerControl != null && _containerControl.Visible)
                entries.Add(_containerControl);
        }

        public void Render(InteractiveSurfaceScript owner,
            List<MySprite> targetSprites,
            RectangleF viewBox,
            float scale,
            float fontScale,
            Sandbox.ModAPI.Ingame.IMyTextSurface surface,
            Color textColor,
            Color backgroundColor,
            Color panelColor,
            Vector2 cursorPosition)
        {
            _sprites.Clear();

            if (Dismissed)
                return;

            EnsureContainer(viewBox);
            _containerControl.ClearChildren();

            var shadowColor = panelColor.MulValue(0.2f);
                
            _sprites.Add(new MySprite(SpriteType.TEXTURE,
                "SquareSimple",
                surface.TextureSize/2,
                surface.TextureSize,
                new Color(0, 0, 0, 128)));

            float titleScale = 0.82f * scale * fontScale;
            float contentScale = 0.58f * scale * fontScale;
            float buttonScale = 0.58f * scale * fontScale;

            Vector2 padding = new Vector2(18f, 14f) * scale;
            float spacing = 10f * scale;
            float buttonSpacing = 10f * scale;
            float buttonHeight = Math.Max(24f * scale, FormatingHelper.LineHeight(buttonScale, surface) + 10f * scale);
            float minButtonWidth = 78f * scale;

            var titleSize = FormatingHelper.GetSizeInPixel(_title, "White", titleScale, surface);
            var contentLines = SplitLines(_content);
            if (contentLines.Length == 0)
                contentLines = new[] { string.Empty };

            float lineStep = FormatingHelper.LineHeight(contentScale, surface) + 2f * scale;
            float maxContentWidth = 0f;
            for (int i = 0; i < contentLines.Length; i++)
            {
                var size = FormatingHelper.GetSizeInPixel(contentLines[i], "White", contentScale, surface);
                if (size.X > maxContentWidth)
                    maxContentWidth = size.X;
            }

            bool hasIcon = !string.IsNullOrEmpty(_icon);
            float iconSize = hasIcon ? Math.Max(32f * scale, lineStep * Math.Min(2.5f, Math.Max(1f, contentLines.Length))) : 0f;
            float iconGap = hasIcon ? 12f * scale : 0f;
            float contentBlockWidth = maxContentWidth + iconSize + iconGap;

            var button1Size = FormatingHelper.GetSizeInPixel(_button1, "White", buttonScale, surface);
            var button2Size = FormatingHelper.GetSizeInPixel(_button2, "White", buttonScale, surface);
            float button1Width = Math.Max(minButtonWidth, button1Size.X + 28f * scale);
            bool showButton2 = _button2Callback != null || !string.IsNullOrWhiteSpace(_button2);
            float button2Width = showButton2 ? Math.Max(minButtonWidth, button2Size.X + 28f * scale) : 0f;
            float buttonsWidth = showButton2 ? button1Width + buttonSpacing + button2Width : button1Width;

            float contentHeight = lineStep * contentLines.Length;
            float cardWidth = Math.Max(240f * scale,
                Math.Max(titleSize.X, Math.Max(contentBlockWidth, buttonsWidth)) + padding.X * 2f);
            cardWidth = Math.Min(cardWidth, viewBox.Width - padding.X * 2f);

            float cardHeight = padding.Y * 2f + titleSize.Y + spacing + Math.Max(contentHeight, iconSize) + spacing + buttonHeight;
            cardHeight = Math.Min(cardHeight, viewBox.Height - padding.Y * 2f);

            var cardRect = new RectangleF(
                viewBox.Center.X - cardWidth * 0.5f,
                viewBox.Center.Y - cardHeight * 0.5f,
                cardWidth,
                cardHeight);

            var shadowRect = new RectangleF(cardRect.Position + 2f, cardRect.Size);
            Border.CreateSpritesFromRect(shadowRect, _sprites, shadowColor, 0.2f);
            Border.CreateSpritesFromRect(cardRect, _sprites, panelColor, 0.2f);

            float currentY = cardRect.Y + padding.Y;

            var titleSprite = new MySprite
            {
                Type = SpriteType.TEXT,
                Data = _title,
                Position = new Vector2(cardRect.Center.X, currentY),
                Color = textColor,
                FontId = "White",
                Alignment = TextAlignment.CENTER,
                RotationOrScale = titleScale
            };

            _sprites.Add(titleSprite.Shadow(2 * titleScale, shadowColor));
            _sprites.Add(titleSprite);

            currentY += titleSize.Y + spacing;

            float contentAreaWidth = cardRect.Width - padding.X * 2f;
            float contentStartX = cardRect.X + padding.X + Math.Max(0f, (contentAreaWidth - contentBlockWidth) * 0.5f);
            float contentTop = currentY;
            float contentMiddleY = contentTop + Math.Max(contentHeight, iconSize) * 0.5f;

            if (hasIcon)
            {
                _sprites.Add(new MySprite
                {
                    Type = SpriteType.TEXTURE,
                    Data = _icon,
                    Position = new Vector2(contentStartX + iconSize * 0.5f, contentMiddleY),
                    Size = new Vector2(iconSize),
                    Color = textColor,
                    Alignment = TextAlignment.CENTER
                });
            }

            float textX = contentStartX + iconSize + iconGap;
            for (int i = 0; i < contentLines.Length; i++)
            {
                _sprites.Add(new MySprite
                {
                    Type = SpriteType.TEXT,
                    Data = contentLines[i],
                    Position = new Vector2(textX, currentY),
                    Color = textColor,
                    FontId = "White",
                    Alignment = TextAlignment.LEFT,
                    RotationOrScale = contentScale
                });

                currentY += lineStep;
            }

            currentY = contentTop + Math.Max(contentHeight, iconSize) + spacing;

            float buttonsStartX = cardRect.Center.X - buttonsWidth * 0.5f;
            var button1Rect = new RectangleF(buttonsStartX, currentY, button1Width, buttonHeight);
            var button2Rect = showButton2
                ? new RectangleF(button1Rect.Right + buttonSpacing, currentY, button2Width, buttonHeight)
                : default(RectangleF);

            EnsureEntries(button1Rect, button2Rect, showButton2);
            _containerControl.AddChild(_button1Control);
            if (showButton2 && _button2Control != null)
                _containerControl.AddChild(_button2Control);

            var renderContext = new ControlRenderContext(surface, scale, fontScale, textColor, panelColor, cursorPosition);
            ConfigureButtonRender(_button1Control, _button1, buttonScale, panelColor, textColor, owner);
            _button1Control.Render(renderContext, _sprites);

            if (showButton2 && _button2Control != null)
            {
                ConfigureButtonRender(_button2Control, _button2, buttonScale, panelColor, textColor, owner);
                _button2Control.Render(renderContext, _sprites);
            }

            targetSprites.AddRange(_sprites);
        }

        void EnsureContainer(RectangleF bounds)
        {
            if (_containerControl == null)
                _containerControl = new MessageBoxContainerControl(bounds, _parentApp);
            else
                _containerControl.SetRect(bounds);

            _containerControl.SetDataContext(_parentApp);
            _containerControl.SetVisible(true);
        }

        static string[] SplitLines(string content)
        {
            if (string.IsNullOrEmpty(content))
                return new[] { string.Empty };

            return content.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        }

        void EnsureEntries(RectangleF button1Rect, RectangleF button2Rect, bool showButton2)
        {
            if (_button1Control == null)
            {
                _button1Control = new RectangleControl(
                    button1Rect,
                    CursorType.Hand,
                    _button1Context,
                    OnButton1Click);
            }
            else
            {
                _button1Control.SetRect(button1Rect);
                _button1Control.SetCursor(CursorType.Hand);
            }

            _button1Control.SetVisible(true);

            if (_button2Control == null)
            {
                _button2Control = new RectangleControl(
                    button2Rect,
                    CursorType.Hand,
                    _button2Context,
                    OnButton2Click);
            }
            else
            {
                _button2Control.SetRect(button2Rect);
                _button2Control.SetCursor(CursorType.Hand);
            }

            _button2Control.SetVisible(showButton2);
        }

        void OnButton1Click(object dataContext, object sender)
        {
            var callback = _button1Callback;
            Dismiss();

            if (callback != null)
                callback(dataContext, sender);
        }

        void OnButton2Click(object dataContext, object sender)
        {
            var callback = _button2Callback;
            Dismiss();

            if (callback != null)
                callback(dataContext, sender);
        }

        void Dismiss()
        {
            Dismissed = true;
            _button1Callback = null;
            _button2Callback = null;
            _sprites.Clear();

            if (_button1Control != null)
                _button1Control.SetVisible(false);
            if (_button2Control != null)
                _button2Control.SetVisible(false);
            if (_containerControl != null)
            {
                _containerControl.ClearChildren();
                _containerControl.SetVisible(false);
            }
        }
            

        static void ConfigureButtonRender(
            RectangleControl control,
            string text,
            float textScale,
            Color panelColor,
            Color textColor,
            InteractiveSurfaceScript owner)
        {
            if (control == null)
                return;

            control.CustomRender = delegate(ControlBase renderEntry, ControlRenderContext context, List<MySprite> sprites)
            {
                DrawButton(renderEntry.Bounds, owner, sprites, text, textScale, panelColor, textColor, context.CursorPosition);
            };
        }

        static void DrawButton(
            RectangleF rect,
            InteractiveSurfaceScript owner,
            List<MySprite> sprites,
            string text,
            float textScale,
            Color panelColor,
            Color textColor,
            Vector2 cursorPosition)
        {
            var hover = rect.Contains(cursorPosition);
            var buttonColor = hover
                ? panelColor.DeriveAccentColor()
                : panelColor.MulValue(0.85f);

            Border.CreateSpritesFromRect(rect, sprites, buttonColor, 0.5f);

            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT,
                Data = text,
                Position = new Vector2(rect.Center.X, rect.Center.Y - FormatingHelper.GetSizeInPixel(text, "White", textScale, owner.Surface).Y * 0.5f),
                Color = hover ? panelColor.MulValue(0.85f) : textColor,
                FontId = "White",
                Alignment = TextAlignment.CENTER,
                RotationOrScale = textScale
            });
        }
    }

    sealed class MessageBoxContainerControl : RectangleControl
    {
        public MessageBoxContainerControl(RectangleF rect, IApp parentApp)
            : base(rect, CursorType.Default, parentApp)
        {
        }

        protected override void RenderDefault(ControlRenderContext context, List<MySprite> sprites)
        {
        }
    }
}
