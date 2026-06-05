using System;
using System.Collections.Generic;
using LcdMod.Client.Gui.ControlsTemplates.Basic;
using LcdMod.Client.Gui.ControlsTemplates.Panels;
using LcdMod.Client.Helpers;
using LcdMod.Common.Helpers;
using VRage.Game.GUI.TextPanel;
using VRageMath;
using IMyTextSurface = Sandbox.ModAPI.Ingame.IMyTextSurface;

namespace LcdMod.Client.Gui.ControlsTemplates
{
    /// <summary>
    /// Draws buttons in the style of ButtonPadApp's buttons: a rounded primary panel with a drop shadow
    /// and hover-aware colours, plus a centred icon (one of the mod's own LCD sprites) or a centred
    /// (auto-wrapped, trimmed) label.
    ///
    /// Everything visual is sized in PROPORTION to the button itself (radius, shadow offset, text), NOT
    /// to the surface auto-fit scale. ButtonPadApp renders at <c>AppConfig.Scale</c> (~1) so a fixed
    /// <c>6px * scale</c> radius stays visible; CargoActions renders at the auto-fit <c>Host.Scale</c>
    /// (~0.1 on a corner LCD), where the same formula collapses radius/shadow/text to zero. Proportional
    /// sizing keeps the buttons identical-looking on a 512² panel and a tiny corner LCD alike.
    /// Pair with <see cref="Button.CreatePrimaryButtonStyle"/>.
    /// </summary>
    internal static class PadButtonStyle
    {
        const float RADIUS_FRACTION = 0.08f;       
        const float SHADOW_FRACTION = 0.025f;      
        const float TEXT_HEIGHT_FRACTION = 0.16f;  
        const float TEXT_MIN_HEIGHT = 10f;         
        const float TEXT_MAX_HEIGHT = 22f;         
        const float TEXT_SIDE_PADDING = 0.10f;     
        const float ICON_FRACTION = 0.92f;         

        public static void RenderLabeled(ControlBase control, ControlRenderContext context, List<MySprite> sprites)
        {
            if (control == null)
                return;

            var tile = control.DataContext as PadTileModel;
            var model = control.DataContext as ButtonModel;
            RenderTile(control.Bounds,
                model != null ? model.Text : null,
                tile != null ? tile.SpriteName : null,
                context, sprites);
        }

        public static void RenderTile(RectangleF rect, string label, string spriteName,
            ControlRenderContext context, List<MySprite> sprites)
        {
            var hovered = rect.Contains(context.CursorPosition);
            var panelColor = context.Style.GetPanelColor(hovered);
            var textColor = context.Style.GetTextColor(hovered);
            var shadowColor = context.GetThemeColor(Constants.SHADOW);

            var minDim = Math.Min(rect.Width, rect.Height);
            var radius = minDim * RADIUS_FRACTION;
            var shadowOffset = Math.Max(1f, minDim * SHADOW_FRACTION);

            Border.CreateSpritesFromRect(
                new RectangleF(rect.X + shadowOffset, rect.Y + shadowOffset, rect.Width, rect.Height),
                sprites, shadowColor, radiusPixels: radius, radiusScale: 1f);
            Border.CreateSpritesFromRect(rect, sprites, panelColor, radiusPixels: radius, radiusScale: 1f);

            if (!string.IsNullOrEmpty(spriteName))
                DrawIcon(rect, spriteName, sprites);
            else if (!string.IsNullOrEmpty(label))
                DrawCenteredLabel(rect, label, textColor, context.Surface, sprites);
        }

        static void DrawIcon(RectangleF rect, string spriteName, List<MySprite> sprites)
        {
            var iconSize = Math.Max(1f, Math.Min(rect.Width, rect.Height) * ICON_FRACTION);

            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXTURE,
                Data = spriteName,
                Position = rect.Center,
                Size = new Vector2(iconSize, iconSize),
                Color = Color.White,
                Alignment = TextAlignment.CENTER
            });
        }

        static void DrawCenteredLabel(RectangleF rect, string label, Color color, IMyTextSurface surface, List<MySprite> sprites)
        {
            var maxWidth = Math.Max(1f, rect.Width - rect.Width * TEXT_SIDE_PADDING * 2f);
            var targetHeight = MathHelper.Clamp(rect.Height * TEXT_HEIGHT_FRACTION, TEXT_MIN_HEIGHT, TEXT_MAX_HEIGHT);
            var textScale = TextScaleForHeight(targetHeight, surface);
            var size = FormatingHelper.GetSizeInPixel(label, "White", textScale, surface);

            if (size.X <= maxWidth)
            {
                var lineHeight = FormatingHelper.LineHeight(textScale, surface);
                AddLine(rect.Center.X, rect.Center.Y - lineHeight * 0.5f, label, textScale, color, sprites);
                return;
            }

            string first, second;
            if (TrySplitAtMiddleSpace(label, out first, out second))
            {
                var twoLineScale = TextScaleForHeight(Math.Max(TEXT_MIN_HEIGHT * 0.85f, targetHeight * 0.78f), surface);
                var lh = FormatingHelper.LineHeight(twoLineScale, surface);
                first = TrimToWidth(first, maxWidth, twoLineScale, surface);
                second = TrimToWidth(second, maxWidth, twoLineScale, surface);
                AddLine(rect.Center.X, rect.Center.Y - lh, first, twoLineScale, color, sprites);
                AddLine(rect.Center.X, rect.Center.Y, second, twoLineScale, color, sprites);
                return;
            }

            var trimmed = TrimToWidth(label, maxWidth, textScale, surface);
            var singleLineHeight = FormatingHelper.LineHeight(textScale, surface);
            AddLine(rect.Center.X, rect.Center.Y - singleLineHeight * 0.5f, trimmed, textScale, color, sprites);
        }

        static void AddLine(float centerX, float topY, string text, float scale, Color color, List<MySprite> sprites)
        {
            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT,
                Data = text,
                Position = new Vector2(centerX, topY),
                Color = color,
                FontId = "White",
                RotationOrScale = scale,
                Alignment = TextAlignment.CENTER
            });
        }

        static bool TrySplitAtMiddleSpace(string text, out string first, out string second)
        {
            first = null;
            second = null;
            if (string.IsNullOrEmpty(text))
                return false;

            int mid = text.Length / 2;
            int best = -1;
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] != ' ')
                    continue;
                if (best < 0 || Math.Abs(i - mid) < Math.Abs(best - mid))
                    best = i;
            }

            if (best <= 0 || best >= text.Length - 1)
                return false;

            first = text.Substring(0, best);
            second = text.Substring(best + 1);
            return true;
        }

        public static float TextScaleForHeight(float targetHeight, IMyTextSurface surface)
        {
            if (surface == null)
                return 0.05f;

            var line = FormatingHelper.LineHeight(1f, surface);
            return Math.Max(0.05f, targetHeight / Math.Max(1f, line));
        }

        public static string TrimToWidth(string text, float maxWidth, float scale, IMyTextSurface surface)
        {
            if (string.IsNullOrEmpty(text) || maxWidth <= 0f || surface == null)
                return text ?? string.Empty;

            var size = FormatingHelper.GetSizeInPixel(text, "White", scale, surface);
            if (size.X <= maxWidth)
                return text;

            return FormatingHelper.TrimName(text, Math.Max(1, (int)(text.Length * maxWidth / Math.Max(1f, size.X))));
        }
    }

    /// <summary>Button model that also carries an optional icon sprite: with a <see cref="SpriteName"/>
    /// <see cref="PadButtonStyle.RenderLabeled"/> draws the icon, without one it draws the text label.</summary>
    internal sealed class PadTileModel : ButtonModel
    {
        public string SpriteName { get; set; }
    }
}
