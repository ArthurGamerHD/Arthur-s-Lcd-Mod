using System;
using System.Collections.Generic;
using System.Text;
using LcdMod.Client.Helpers;
using LcdMod.Client.SurfaceScripts.Abstract;
using VRage.Collections;
using VRage.Game.GUI.TextPanel;
using VRage.Utils;
using VRageMath;

namespace LcdMod.Client.Gui.Controls
{
    class BSoD
    {
        const string FONT_ID = "DEBUG";
        const int QR_LOGICAL_SIZE = 52;
        const int QR_INNER_LOGICAL_SIZE = 50;
        const float QR_SPRITE_UNIT_SCALE = 10f;


        readonly Sandbox.ModAPI.Ingame.IMyTextSurface _surface;
        readonly float _scale;
        readonly Vector2 _centerPos;
        readonly List<MySprite> _frame = new List<MySprite>();
        public ListReader<MySprite> Frame { get; }

        BSoD(SurfaceScriptBase app, Exception exception)
        {
            var viewBox = app.ViewBox;
            _surface = app.Surface;
            var viewClip = new Rectangle(
                (int)viewBox.X,
                (int)viewBox.Y,
                Math.Max(1, (int)viewBox.Width),
                Math.Max(1, (int)viewBox.Height));

            var white = new Color(179, 237, 255);
            var blue = new Color(0, 88, 151);

            _frame.Add(MySprite.CreateClipRect(viewClip));
            DrawRectangle(viewBox.Center, new Vector2(viewBox.Width, viewBox.Height), blue);

            string headerText = GetLocalizedText(
                "LcdMod_BSoD_Header",
                "Your Station ran into a problem and needs to Restart. We're waiting for a while, and then we'll restart it for you");
            var infoText = GetLocalizedText("LcdMod_BSoD_InfoIntro", "For more information about this issue,") + " " +
                           GetLocalizedText("LcdMod_BSoD_InfoUrl", "Visit https://github.com/ArthurGamerHD/Arthur-s-Lcd-Mod") + " " +
                           GetLocalizedText("LcdMod_BSoD_InfoQr", "or read this QR code.");
            var exceptionText = 
                GetLocalizedText("LcdMod_BSoD_SupportIntro", "If you call a support person, give them this info:") +
                "\n" +
                GetLocalizedText("LcdMod_BSoD_ExceptionCode", "Exception code:") + "\n" + exception;

            float layoutScale = Math.Max(0.45f, Math.Min(viewBox.Width, viewBox.Height) / 512f);
            float outerPadding = RoundToPixel(16f * layoutScale);
            float titleGap = RoundToPixel(8f * layoutScale);
            float sectionGap = RoundToPixel(10f * layoutScale);
            float contentGap = RoundToPixel(12f * layoutScale);
            float sadFace = 3f * layoutScale;
            float headerScale = 0.8f * layoutScale;
            float infoScale = 0.55f * layoutScale;
            float exceptionScale = 0.45f * layoutScale;

            float viewLeft = viewBox.X;
            float viewTop = viewBox.Y;
            float viewRight = viewBox.X + viewBox.Width;
            float viewBottom = viewBox.Y + viewBox.Height;
            float cursorY = viewTop + outerPadding;

            string sadFaceTitle = ":(";
            float titleTextHeight = Math.Max(1f, MeasureText(sadFaceTitle, sadFace).Y);
            float titleRowHeight = titleTextHeight;
            float titleTextY = cursorY + (titleRowHeight - titleTextHeight) * 0.5f;
            float titleTextX = viewLeft + outerPadding;

            _frame.Add(new MySprite
            {
                Type = SpriteType.TEXT,
                Data = sadFaceTitle,
                Color = white,
                FontId = FONT_ID,
                Alignment = TextAlignment.LEFT,
                Position = RoundToPixel(new Vector2(titleTextX, titleTextY)),
                RotationOrScale = sadFace
            });

            cursorY += titleRowHeight + titleGap;

            float availableWidth = Math.Max(8f, viewBox.Width - outerPadding * 2f);
            float headerHeight = Math.Max(8f, viewBottom - outerPadding - cursorY);
            var headerLines = WrapLines(headerText, availableWidth, headerScale, headerHeight, false);
            DrawMultilineText(headerLines, viewLeft + outerPadding, cursorY, headerScale, white);
            cursorY += GetLinesHeight(headerLines, headerScale) + sectionGap;

            float remainingHeightForContent = Math.Max(52f, viewBottom - outerPadding - cursorY);
            float requestedQrSize = Math.Min(availableWidth * 0.24f, remainingHeightForContent * 0.42f);
            float quantizedQr = QuantizeQrSize(requestedQrSize, requestedQrSize);
            float qrSize = RoundToPixel(Math.Max(40f * layoutScale, Math.Min(requestedQrSize, quantizedQr)));
            var qrRect = CreateIntegerQrRect(
                viewLeft + outerPadding,
                cursorY,
                qrSize);

            _scale = GetQrScale(qrRect.Width);
            _centerPos = qrRect.Center;
            DrawQr(white, blue);

            float infoX = qrRect.Right + contentGap;
            float infoWidth = Math.Max(8f, viewRight - outerPadding - infoX);
            float infoHeight = qrRect.Height;

            var infoLines = WrapLines(infoText, infoWidth, infoScale, infoHeight, false);
            DrawMultilineText(infoLines, infoX, qrRect.Y, infoScale, white);

            cursorY = qrRect.Bottom + sectionGap;

            float exceptionHeight = Math.Max(8f, viewBottom - outerPadding - cursorY);
            var exceptionLines = WrapLines(exceptionText, availableWidth, exceptionScale, exceptionHeight, true);
            DrawMultilineText(exceptionLines, viewLeft + outerPadding, cursorY, exceptionScale, white);

            _frame.Add(MySprite.CreateClearClipRect());
            Frame = _frame;
        }

        static string GetLocalizedText(string loc, string fallback)
        {
            try
            {
                string localized = LocHelper.GetLoc(loc);
                if (localized != null && !string.Equals(localized, loc, StringComparison.Ordinal))
                    return localized;
            }
            catch
            {
                // Intentionally ignored: BSoD must be able to render even if localization breaks.
            }

            return fallback;
        }

        static float QuantizeQrSize(float requestedSize, float maxSize)
        {
            float safeMaxSize = Math.Max(QR_LOGICAL_SIZE, maxSize);
            float moduleSize = Math.Max(1f,
                (float)Math.Round(requestedSize / QR_LOGICAL_SIZE, MidpointRounding.AwayFromZero));
            float quantizedSize = moduleSize * QR_LOGICAL_SIZE;
            if (quantizedSize > safeMaxSize)
            {
                moduleSize = Math.Max(1f, (float)Math.Floor(safeMaxSize / QR_LOGICAL_SIZE));
                quantizedSize = moduleSize * QR_LOGICAL_SIZE;
            }

            return quantizedSize;
        }

        static float GetQrScale(float qrSize)
        {
            return Math.Max(0.1f, qrSize / QR_LOGICAL_SIZE) / QR_SPRITE_UNIT_SCALE;
        }

        static RectangleF CreateIntegerQrRect(float x, float y, float size)
        {
            float integerSize = RoundToPixel(size);
            return new RectangleF(
                RoundToPixel(x),
                RoundToPixel(y),
                integerSize,
                integerSize);
        }

        static Vector2 RoundToPixel(Vector2 value)
        {
            return new Vector2(RoundToPixel(value.X), RoundToPixel(value.Y));
        }

        static float RoundToPixel(float value)
        {
            return (float)Math.Round(value);
        }

        static float ClampSafe(float value, float min, float max)
        {
            if (max < min)
                return value;

            return MathHelper.Clamp(value, min, max);
        }

        Vector2 MeasureText(string text, float scale)
        {
            return FormatingHelper.GetSizeInPixel(text, FONT_ID, scale, _surface);
        }

        List<string> WrapLines(string text, float maxWidth, float scale, float maxHeight, bool trimLastLine)
        {
            var result = new List<string>();
            if (string.IsNullOrEmpty(text) || maxWidth <= 1f || maxHeight <= 1f)
                return result;

            string normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
            float lineHeight = Math.Max(1f, MeasureText("Ag", scale).Y + 2f);
            int maxLines = (int)Math.Floor(maxHeight / lineHeight);
            if (maxLines < 1)
                return result;

            var paragraphs = normalized.Split('\n');
            for (int paragraphIndex = 0; paragraphIndex < paragraphs.Length; paragraphIndex++)
            {
                string paragraph = paragraphs[paragraphIndex];
                if (string.IsNullOrEmpty(paragraph))
                {
                    if (result.Count < maxLines)
                        result.Add(string.Empty);
                    continue;
                }

                var words = paragraph.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                string current = string.Empty;
                for (int i = 0; i < words.Length; i++)
                {
                    string candidate = string.IsNullOrEmpty(current) ? words[i] : current + " " + words[i];
                    if (MeasureText(candidate, scale).X <= maxWidth)
                    {
                        current = candidate;
                        continue;
                    }

                    if (!string.IsNullOrEmpty(current))
                    {
                        result.Add(current);
                        if (result.Count >= maxLines)
                            return TrimLineOverflow(result, maxWidth, scale, trimLastLine);
                        current = string.Empty;
                        i--;
                        continue;
                    }

                    string word = words[i];
                    int split = word.Length;
                    while (split > 1 && MeasureText(word.Substring(0, split), scale).X > maxWidth)
                        split--;
                    result.Add(split <= 1 ? TrimToWidth(word, maxWidth, scale) : word.Substring(0, split));
                    if (result.Count >= maxLines)
                        return TrimLineOverflow(result, maxWidth, scale, trimLastLine);
                    if (split < word.Length)
                        words[i] = word.Substring(split);
                }

                if (!string.IsNullOrEmpty(current))
                {
                    result.Add(current);
                    if (result.Count >= maxLines)
                        return TrimLineOverflow(result, maxWidth, scale, trimLastLine);
                }
            }

            return TrimLineOverflow(result, maxWidth, scale, trimLastLine);
        }

        List<string> TrimLineOverflow(List<string> lines, float maxWidth, float scale, bool trimLastLine)
        {
            if (lines == null || lines.Count == 0)
                return lines;

            for (int i = 0; i < lines.Count; i++)
                lines[i] = TrimToWidth(lines[i], maxWidth, scale);

            if (trimLastLine && lines.Count > 0)
                lines[lines.Count - 1] = EnsureEllipsis(lines[lines.Count - 1], maxWidth, scale);

            return lines;
        }

        string TrimToWidth(string input, float maxWidth, float scale)
        {
            if (string.IsNullOrEmpty(input))
                return string.Empty;
            if (MeasureText(input, scale).X <= maxWidth)
                return input;

            var sb = new StringBuilder(input);
            while (sb.Length > 1)
            {
                sb.Length--;
                if (MeasureText(sb.ToString(), scale).X <= maxWidth)
                    return sb.ToString();
            }

            return string.Empty;
        }

        string EnsureEllipsis(string input, float maxWidth, float scale)
        {
            string suffix = FormatingHelper.ELLIPSIS.ToString();
            string trimmed = TrimToWidth((input ?? string.Empty).TrimEnd(), maxWidth, scale);
            if (string.IsNullOrEmpty(trimmed))
                return suffix;
            if (trimmed.EndsWith(suffix, StringComparison.Ordinal))
                return trimmed;
            return TrimToWidth(trimmed + suffix, maxWidth, scale);
        }

        void DrawMultilineText(List<string> lines, float x, float y, float scale, Color color)
        {
            if (lines == null || lines.Count == 0)
                return;

            float lineHeight = Math.Max(1f, MeasureText("Ag", scale).Y + 2f);
            for (int i = 0; i < lines.Count; i++)
            {
                _frame.Add(new MySprite
                {
                    Type = SpriteType.TEXT,
                    Data = lines[i],
                    Position = RoundToPixel(new Vector2(x, y + i * lineHeight)),
                    Color = color,
                    FontId = FONT_ID,
                    Alignment = TextAlignment.LEFT,
                    RotationOrScale = scale
                });
            }
        }

        float GetLinesHeight(List<string> lines, float scale)
        {
            if (lines == null || lines.Count == 0)
                return 0f;
            float lineHeight = Math.Max(1f, MeasureText("Ag", scale).Y + 2f);
            return lineHeight * lines.Count;
        }

        void DrawRectangle(Vector2 center, Vector2 size, Color color)
        {
            if (size.X <= 0f || size.Y <= 0f || color.A == 0)
                return;

            _frame.Add(new MySprite(
                SpriteType.TEXTURE,
                "SquareSimple",
                center,
                size,
                color));
        }
        
        void DrawRectangle(float positionX, float positionY, float sizeX, float sizeY, Color color)
        {
            var moduleSize = QR_SPRITE_UNIT_SCALE * _scale;
            var position = RoundToPixel(new Vector2(positionX, positionY) * moduleSize + _centerPos);
            var size = RoundToPixel(new Vector2(sizeX, sizeY) * moduleSize);
            if (size.X <= 0f || size.Y <= 0f)
                return;

            _frame.Add(new MySprite(
                SpriteType.TEXTURE,
                "SquareSimple",
                position,
                size,
                color));
        }

        void DrawQr(Color w, Color b)
        {
            DrawRectangle(0f, 0f, QR_LOGICAL_SIZE, QR_LOGICAL_SIZE, w);
            DrawRectangle(0f, 0f, QR_INNER_LOGICAL_SIZE, QR_INNER_LOGICAL_SIZE, b);
            DrawRectangle(18f, -18f, 10f, 10f, w);
            DrawRectangle(-18f, -18f, 10f, 10f, w);
            DrawRectangle(-18f, 18f, 10f, 10f, w);
            DrawRectangle(18f, -18f, 6f, 6f, b);
            DrawRectangle(-18f, -18f, 6f, 6f, b);
            DrawRectangle(-18f, 18f, 6f, 6f, b);
            DrawRectangle(17f, -10f, 16f, 2f, w);
            DrawRectangle(-17f, -10f, 16f, 2f, w);
            DrawRectangle(-17f, 10f, 16f, 2f, w);
            DrawRectangle(10f, -17f, 2f, 16f, w);
            DrawRectangle(-10f, -17f, 2f, 16f, w);
            DrawRectangle(-4f, 14f, 2f, 2f, w);
            DrawRectangle(-10f, 17f, 2f, 16f, w);
            DrawRectangle(-6f, 17f, 2f, 4f, w);
            DrawRectangle(-3f, 21f, 4f, 8f, w);
            DrawRectangle(7f, 20f, 4f, 2f, w);
            DrawRectangle(1f, -9f, 4f, 4f, w);
            DrawRectangle(1f, -3f, 4f, 4f, w);
            DrawRectangle(22f, -8f, 2f, 2f, w);
            DrawRectangle(-8f, 22f, 2f, 2f, w);
            DrawRectangle(24f, -7f, 2f, 4f, w);
            DrawRectangle(24f, 10f, 2f, 2f, w);
            DrawRectangle(-2f, 5f, 2f, 8f, w);
            DrawRectangle(9f, -2f, 4f, 2f, w);
            DrawRectangle(14f, 1f, 2f, 4f, w);
            DrawRectangle(24f, -1f, 2f, 4f, w);
            DrawRectangle(18f, 0f, 2f, 2f, w);
            DrawRectangle(-8f, 12f, 2f, 2f, w);
            DrawRectangle(19f, -4f, 4f, 6f, w);
            DrawRectangle(16f, -6f, 2f, 2f, w);
            DrawRectangle(12f, -6f, 2f, 2f, w);
            DrawRectangle(10f, -7f, 2f, 4f, w);
            DrawRectangle(6f, -11f, 2f, 4f, w);
            DrawRectangle(6f, -20f, 2f, 2f, w);
            DrawRectangle(8f, -18f, 2f, 2f, w);
            DrawRectangle(0f, 20f, 2f, 2f, w);
            DrawRectangle(4f, -16f, 6f, 2f, w);
            DrawRectangle(4f, -14f, 2f, 2f, w);
            DrawRectangle(2f, -12f, 2f, 2f, w);
            DrawRectangle(-2f, -12f, 2f, 2f, w);
            DrawRectangle(0f, -14f, 2f, 2f, w);
            DrawRectangle(4f, -4f, 2f, 2f, w);
            DrawRectangle(2f, -6f, 2f, 2f, w);
            DrawRectangle(2f, 24f, 2f, 2f, w);
            DrawRectangle(10f, 4f, 2f, 2f, w);
            DrawRectangle(6f, 2f, 6f, 2f, w);
            DrawRectangle(3f, 0f, 8f, 2f, w);
            DrawRectangle(5f, 6f, 4f, 2f, w);
            DrawRectangle(23f, 2f, 4f, 2f, w);
            DrawRectangle(5f, 22f, 4f, 2f, w);
            DrawRectangle(4f, 12f, 6f, 2f, w);
            DrawRectangle(18f, 4f, 6f, 6f, w);
            DrawRectangle(12f, 12f, 6f, 6f, w);
            DrawRectangle(18f, 4f, 2f, 2f, b);
            DrawRectangle(12f, 12f, 2f, 2f, b);
            DrawRectangle(2f, 16f, 6f, 2f, w);
            DrawRectangle(20f, 22f, 6f, 2f, w);
            DrawRectangle(14f, 24f, 6f, 2f, w);
            DrawRectangle(-20f, 1f, 2f, 8f, w);
            DrawRectangle(-24f, -3f, 2f, 8f, w);
            DrawRectangle(-16f, 4f, 2f, 6f, w);
            DrawRectangle(-8f, 5f, 2f, 4f, w);
            DrawRectangle(-12f, 6f, 2f, 2f, w);
            DrawRectangle(-10f, 8f, 2f, 2f, w);
            DrawRectangle(-14f, 8f, 2f, 2f, w);
            DrawRectangle(-18f, 7f, 2f, 4f, w);
            DrawRectangle(-22f, 6f, 2f, 6f, w);
            DrawRectangle(18f, 18f, 2f, 2f, w);
            DrawRectangle(14f, 18f, 2f, 2f, w);
            DrawRectangle(14f, 22f, 2f, 2f, w);
            DrawRectangle(16f, 20f, 2f, 2f, w);
            DrawRectangle(12f, 20f, 2f, 2f, w);
            DrawRectangle(8f, 18f, 2f, 2f, w);
            DrawRectangle(8f, 24f, 2f, 2f, w);
            DrawRectangle(-12f, -6f, 2f, 2f, w);
            DrawRectangle(19f, 11f, 4f, 4f, w);
            DrawRectangle(23f, 8f, 4f, 2f, w);
            DrawRectangle(21f, 14f, 4f, 2f, w);
            DrawRectangle(-17f, -6f, 4f, 2f, w);
            DrawRectangle(-9f, -8f, 4f, 2f, w);
            DrawRectangle(1f, 14f, 4f, 2f, w);
            DrawRectangle(-15f, 2f, 8f, 2f, w);
            DrawRectangle(-14f, -2f, 6f, 2f, w);
            DrawRectangle(-4f, -23f, 2f, 4f, w);
            DrawRectangle(-8f, -23f, 2f, 4f, w);
            DrawRectangle(-6f, -15f, 2f, 8f, w);
            DrawRectangle(-6f, -4f, 2f, 6f, w);
            DrawRectangle(-8f, -4f, 2f, 2f, w);
            DrawRectangle(-2f, -5f, 2f, 4f, w);
            DrawRectangle(-4f, -9f, 2f, 4f, w);
            DrawRectangle(0f, 4f, 2f, 2f, w);
            DrawRectangle(2f, 8f, 2f, 2f, w);
            DrawRectangle(-6f, 8f, 2f, 2f, w);
            DrawRectangle(-8f, 0f, 2f, 2f, w);
            DrawRectangle(0f, -19f, 2f, 4f, w);
            DrawRectangle(0f, 10f, 14f, 2f, w);
            DrawRectangle(3f, -21f, 4f, 4f, w);
            DrawRectangle(-3f, -17f, 4f, 4f, w);
            DrawRectangle(-2f, -22f, 2f, 2f, w);
            DrawRectangle(-10f, 4f, 2f, 2f, w);
            DrawRectangle(-14f, 4f, 2f, 2f, w);
            DrawRectangle(-14f, -4f, 2f, 2f, w);
            DrawRectangle(-18f, -4f, 2f, 2f, w);
            DrawRectangle(-22f, -2f, 2f, 2f, w);
            DrawRectangle(6f, 14f, 2f, 2f, w);
            DrawRectangle(22f, 20f, 2f, 2f, w);
            DrawRectangle(-22f, -8f, 2f, 2f, w);
        }

        public static BSoD ShowBSoD(SurfaceScriptBase surfaceScriptBase, Exception exception) =>
            new BSoD(surfaceScriptBase, exception);
    }
}
