using System;
using System.Collections.Generic;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.Extensions;
using LcdMod.Client.Gui.ControlsTemplates.Basic;
using LcdMod.Client.Gui.ControlsTemplates.Panels;
using LcdMod.Client.Helpers;
using LcdMod.Common.Helpers;
using VRage.Game.GUI.TextPanel;
using VRageMath;
using InteractiveSurfaceScript = LcdMod.Client.SurfaceScripts.Abstract.InteractiveSurfaceScript;

namespace LcdMod.Client.Gui.ControlsTemplates.Interactive
{
    sealed class MessageBox : Dialog
    {
        readonly object _button1Context = new object();
        readonly object _button2Context = new object();

        RectangleControl _button1Control;
        RectangleControl _button2Control;
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
            _button1 = string.IsNullOrEmpty(button1) ? "OK" : button1;
            _button2 = button2 ?? string.Empty;
            _icon = icon;

            _button1Callback = button1Callback;
            _button2Callback = button2Callback;
        }

        protected override void RenderCore(
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
            ContainerControl.ClearChildren();

            var cardColor = GetThemeColor(Constants.SURFACE_CONTAINER_HIGH);
            var cardTextColor = GetThemeColor(Constants.ON_SURFACE);
            var shadowColor = GetThemeColor(Constants.SHADOW);
                
            Sprites.Add(new MySprite(SpriteType.TEXTURE,
                "SquareSimple",
                surface.TextureSize/2,
                surface.TextureSize,
                new Color(0, 0, 0, 128)));

            var titleScale = 0.82f * scale * fontScale;
            var contentScale = 0.58f * scale * fontScale;
            var buttonScale = 0.58f * scale * fontScale;

            var padding = new Vector2(18f, 14f) * scale;
            var spacing = 10f * scale;
            var buttonSpacing = 10f * scale;
            var buttonHeight = Math.Max(24f * scale, FormatingHelper.LineHeight(buttonScale, surface) + 10f * scale);
            var minButtonWidth = 78f * scale;

            var titleSize = FormatingHelper.GetSizeInPixel(_title, "White", titleScale, surface);
            var contentLines = SplitLines(_content);
            if (contentLines.Length == 0)
                contentLines = new[] { string.Empty };

            var lineStep = FormatingHelper.LineHeight(contentScale, surface) + 2f * scale;
            var maxContentWidth = 0f;
            for (var i = 0; i < contentLines.Length; i++)
            {
                var size = FormatingHelper.GetSizeInPixel(contentLines[i], "White", contentScale, surface);
                if (size.X > maxContentWidth)
                    maxContentWidth = size.X;
            }

            var hasIcon = !string.IsNullOrEmpty(_icon);
            var iconSize = hasIcon ? Math.Max(32f * scale, lineStep * Math.Min(2.5f, Math.Max(1f, contentLines.Length))) : 0f;
            var iconGap = hasIcon ? 12f * scale : 0f;
            var contentBlockWidth = maxContentWidth + iconSize + iconGap;

            var button1Size = FormatingHelper.GetSizeInPixel(_button1, "White", buttonScale, surface);
            var button2Size = FormatingHelper.GetSizeInPixel(_button2, "White", buttonScale, surface);
            var button1Width = Math.Max(minButtonWidth, button1Size.X + 28f * scale);
            var showButton2 = _button2Callback != null || !string.IsNullOrWhiteSpace(_button2);
            var button2Width = showButton2 ? Math.Max(minButtonWidth, button2Size.X + 28f * scale) : 0f;
            var buttonsWidth = showButton2 ? button1Width + buttonSpacing + button2Width : button1Width;

            var contentHeight = lineStep * contentLines.Length;
            var cardWidth = Math.Max(240f * scale,
                Math.Max(titleSize.X, Math.Max(contentBlockWidth, buttonsWidth)) + padding.X * 2f);
            cardWidth = Math.Min(cardWidth, viewBox.Width - padding.X * 2f);

            var cardHeight = padding.Y * 2f + titleSize.Y + spacing + Math.Max(contentHeight, iconSize) + spacing + buttonHeight;
            cardHeight = Math.Min(cardHeight, viewBox.Height - padding.Y * 2f);

            var cardRect = new RectangleF(
                viewBox.Center.X - cardWidth * 0.5f,
                viewBox.Center.Y - cardHeight * 0.5f,
                cardWidth,
                cardHeight);

            var shadowRect = new RectangleF(cardRect.Position + 2f, cardRect.Size);
            Border.CreateSpritesFromRect(shadowRect, Sprites, shadowColor, 0.2f);
            Border.CreateSpritesFromRect(cardRect, Sprites, cardColor, 0.2f);

            var currentY = cardRect.Y + padding.Y;

            var titleSprite = new MySprite
            {
                Type = SpriteType.TEXT,
                Data = _title,
                Position = new Vector2(cardRect.Center.X, currentY),
                Color = cardTextColor,
                FontId = "White",
                Alignment = TextAlignment.CENTER,
                RotationOrScale = titleScale
            };

            Sprites.Add(titleSprite.Shadow(2 * titleScale, shadowColor));
            Sprites.Add(titleSprite);

            currentY += titleSize.Y + spacing;

            var contentAreaWidth = cardRect.Width - padding.X * 2f;
            var contentStartX = cardRect.X + padding.X + Math.Max(0f, (contentAreaWidth - contentBlockWidth) * 0.5f);
            var contentTop = currentY;
            var contentMiddleY = contentTop + Math.Max(contentHeight, iconSize) * 0.5f;

            if (hasIcon)
            {
                Sprites.Add(new MySprite
                {
                    Type = SpriteType.TEXTURE,
                    Data = _icon,
                    Position = new Vector2(contentStartX + iconSize * 0.5f, contentMiddleY),
                    Size = new Vector2(iconSize),
                    Color = cardTextColor,
                    Alignment = TextAlignment.CENTER
                });
            }

            var textX = contentStartX + iconSize + iconGap;
            for (var i = 0; i < contentLines.Length; i++)
            {
                Sprites.Add(new MySprite
                {
                    Type = SpriteType.TEXT,
                    Data = contentLines[i],
                    Position = new Vector2(textX, currentY),
                    Color = cardTextColor,
                    FontId = "White",
                    Alignment = TextAlignment.LEFT,
                    RotationOrScale = contentScale
                });

                currentY += lineStep;
            }

            currentY = contentTop + Math.Max(contentHeight, iconSize) + spacing;

            var buttonsStartX = cardRect.Center.X - buttonsWidth * 0.5f;
            var button1Rect = new RectangleF(buttonsStartX, currentY, button1Width, buttonHeight);
            var button2Rect = showButton2
                ? new RectangleF(button1Rect.Right + buttonSpacing, currentY, button2Width, buttonHeight)
                : default(RectangleF);

            EnsureEntries(button1Rect, button2Rect, showButton2);
            ContainerControl.AddChild(_button1Control);
            if (showButton2 && _button2Control != null)
                ContainerControl.AddChild(_button2Control);

            var renderContext = CreateRenderContext(surface, scale, fontScale, textColor, panelColor, cursorPosition);
            ConfigureButtonRender(_button1Control, _button1, buttonScale, ThemedParentApp, owner);
            _button1Control.Render(renderContext, Sprites);

            if (showButton2 && _button2Control != null)
            {
                ConfigureButtonRender(_button2Control, _button2, buttonScale, ThemedParentApp, owner);
                _button2Control.Render(renderContext, Sprites);
            }
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

        protected override void OnDismiss()
        {
            _button1Callback = null;
            _button2Callback = null;

            if (_button1Control != null)
                _button1Control.SetVisible(false);
            if (_button2Control != null)
                _button2Control.SetVisible(false);
        }
            

        static void ConfigureButtonRender(
            RectangleControl control,
            string text,
            float textScale,
            IThemedApp themedParentApp,
            InteractiveSurfaceScript owner)
        {
            if (control == null)
                return;

            control.SetStyle(Button.CreatePrimaryButtonStyle(themedParentApp?.Theme));
            control.CustomRender = delegate(ControlBase renderEntry, ControlRenderContext context, List<MySprite> sprites)
            {
                DrawButton(renderEntry.Bounds, owner, sprites, text, textScale, context);
            };
        }

        static void DrawButton(
            RectangleF rect,
            InteractiveSurfaceScript owner,
            List<MySprite> sprites,
            string text,
            float textScale,
            ControlRenderContext context)
        {
            var hover = rect.Contains(context.CursorPosition);
            var buttonColor = context.Style.GetPanelColor(hover);
            var buttonTextColor = context.Style.GetTextColor(hover);

            Border.CreateSpritesFromRect(rect, sprites, buttonColor, context.Style.BorderPercentage);

            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT,
                Data = text,
                Position = new Vector2(rect.Center.X, rect.Center.Y - FormatingHelper.GetSizeInPixel(text, "White", textScale, owner.Surface).Y * 0.5f),
                Color = buttonTextColor,
                FontId = "White",
                Alignment = TextAlignment.CENTER,
                RotationOrScale = textScale
            });
        }
    }
}
