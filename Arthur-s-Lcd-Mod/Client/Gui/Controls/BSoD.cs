using System;
using System.Collections.Generic;
using System.Text;
using LcdMod.Client.Apps.Abstract;
using VRage.Collections;
using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace LcdMod.Client.Gui.Controls
{
    class BSoD
    {
        const string FONT_ID = "DEBUG";
        const int QR_LOGICAL_SIZE = 52;
        const int QR_INNER_LOGICAL_SIZE = 50;
        const float QR_SPRITE_UNIT_SCALE = 10f;

        readonly float _scale;
        readonly Vector2 _centerPos;
        readonly List<MySprite> _frame = new List<MySprite>();
        public ListReader<MySprite> Frame { get; }

        BSoD(SurfaceScriptBase app, Exception exception)
        {
            var viewBox = GetViewBox(app);
            var viewClip = new Rectangle(
                (int)viewBox.X,
                (int)viewBox.Y,
                Math.Max(1, (int)viewBox.Width),
                Math.Max(1, (int)viewBox.Height));

            var white = new Color(253, 253, 253);
            var blue = new Color(6, 119, 214);
            float shortSide = Math.Max(1f, Math.Min(viewBox.Width, viewBox.Height));
            bool ultraWide = viewBox.Width >= 2f * viewBox.Height;
            float layoutScale = app.Scale * (ultraWide ? 2f : 1f);
            float margin = ClampSafe(shortSide * 0.04f, 8f * app.Scale, 24f * layoutScale);
            float titleScale = Math.Max(0.30f, layoutScale);
            float bodyScale = Math.Max(0.25f, titleScale * 0.5f);
            float textLeft = viewBox.X + margin;
            float textRight = viewBox.Right - margin;
            float textWidth = Math.Max(1f, textRight - textLeft);
            float y = viewBox.Y + Math.Max(margin, viewBox.Height * 0.14f);

            _frame.Add(MySprite.CreateClipRect(viewClip));
            DrawRectangle(viewBox.Center, new Vector2(viewBox.Width, viewBox.Height), blue);

            y = DrawWrappedText(
                app,
                "Your Station ran into a problem and needs to Restart. We're waiting for a while, And then we'll restart if for you",
                new Vector2(textLeft, y),
                textWidth,
                titleScale,
                1.15f,
                Color.White);

            y += margin * 1.5f;

            float qrTargetSize = Math.Min(shortSide * 0.26f, 120f * layoutScale);
            float qrMaxSize = Math.Max(QR_LOGICAL_SIZE, shortSide - margin * 2f);
            float qrMinSize = Math.Min(qrMaxSize, 40f * app.Scale);
            float qrSize = Math.Max(qrMinSize, Math.Min(qrTargetSize, qrMaxSize));
            qrSize = QuantizeQrSize(qrSize, qrMaxSize);
            bool sideBySide = viewBox.Width - margin * 3f - qrSize >= 160f * bodyScale;
            var infoText = "For more information about this issue,\n" +
                           "visit https://github.com/ArthurGamerHD/Arthur-s-Lcd-Mod\n" +
                           "Or read this QR code";
            var exceptionText = "If you call a support person, give them this info:\nException code:\n" + exception;
            RectangleF qrRect;

            if (sideBySide)
            {
                qrRect = CreateIntegerQrRect(textLeft, y, qrSize);
                float infoLeft = qrRect.Right + margin;
                float infoWidth = Math.Max(1f, textRight - infoLeft);
                float infoBottom = DrawWrappedText(
                    app,
                    infoText,
                    new Vector2(infoLeft, y),
                    infoWidth,
                    bodyScale,
                    1.1f,
                    Color.White);

                _centerPos = qrRect.Center;
                _scale = GetQrScale(qrSize);
                DrawQr(white, blue);

                y = Math.Max(infoBottom, qrRect.Bottom) + margin;
            }
            else
            {
                y = DrawWrappedText(
                    app,
                    infoText,
                    new Vector2(textLeft, y),
                    textWidth,
                    bodyScale,
                    1.1f,
                    Color.White) + margin;

                qrRect = CreateIntegerQrRect(textLeft, y, qrSize);
                _centerPos = qrRect.Center;
                _scale = GetQrScale(qrSize);
                DrawQr(white, blue);
                y = qrRect.Bottom + margin;
            }

            DrawWrappedText(
                app,
                exceptionText,
                new Vector2(textLeft, y),
                textWidth,
                bodyScale,
                1.1f,
                Color.White);

            _frame.Add(MySprite.CreateClearClipRect());
            Frame = _frame;
        }

        static RectangleF GetViewBox(SurfaceScriptBase app)
        {
            var surface = app.Surface;
            var sizeOffset = (surface.TextureSize - surface.SurfaceSize) / 2f;
            var padding = (surface.TextPadding / 100f) * surface.SurfaceSize;
            sizeOffset += padding / 2f;
            return new RectangleF(
                sizeOffset.X,
                sizeOffset.Y,
                Math.Max(1f, surface.SurfaceSize.X - padding.X),
                Math.Max(1f, surface.SurfaceSize.Y - padding.Y));
        }

        float DrawWrappedText(
            SurfaceScriptBase app,
            string text,
            Vector2 position,
            float maxWidth,
            float scale,
            float lineSpacing,
            Color color)
        {
            var lines = WrapText(app, text, maxWidth, scale);
            float lineHeight = GetLineHeight(app, scale) * lineSpacing;
            float y = position.Y;

            foreach (var line in lines)
            {
                _frame.Add(new MySprite(
                    SpriteType.TEXT,
                    line,
                    new Vector2(position.X, y),
                    null,
                    color,
                    FONT_ID,
                    TextAlignment.LEFT,
                    scale));
                y += lineHeight;
            }

            return y;
        }

        static List<string> WrapText(SurfaceScriptBase app, string text, float maxWidth, float scale)
        {
            var result = new List<string>();
            if (string.IsNullOrEmpty(text))
            {
                result.Add(string.Empty);
                return result;
            }

            var paragraphs = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            for (int p = 0; p < paragraphs.Length; p++)
            {
                WrapParagraph(app, paragraphs[p], maxWidth, scale, result);
                if (p < paragraphs.Length - 1 && paragraphs[p].Length == 0)
                    result.Add(string.Empty);
            }

            return result;
        }

        static void WrapParagraph(
            SurfaceScriptBase app,
            string paragraph,
            float maxWidth,
            float scale,
            List<string> result)
        {
            if (string.IsNullOrWhiteSpace(paragraph))
            {
                result.Add(string.Empty);
                return;
            }

            string line = string.Empty;
            var words = paragraph.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < words.Length; i++)
            {
                string word = words[i];
                string candidate = string.IsNullOrEmpty(line) ? word : line + " " + word;
                if (MeasureTextWidth(app, candidate, scale) <= maxWidth)
                {
                    line = candidate;
                    continue;
                }

                if (!string.IsNullOrEmpty(line))
                {
                    result.Add(line);
                }

                if (MeasureTextWidth(app, word, scale) <= maxWidth)
                {
                    line = word;
                    continue;
                }

                line = BreakLongWord(app, word, maxWidth, scale, result);
            }

            if (!string.IsNullOrEmpty(line))
                result.Add(line);
        }

        static string BreakLongWord(
            SurfaceScriptBase app,
            string word,
            float maxWidth,
            float scale,
            List<string> result)
        {
            string line = string.Empty;
            for (int i = 0; i < word.Length; i++)
            {
                string candidate = line + word[i];
                if (!string.IsNullOrEmpty(line) && MeasureTextWidth(app, candidate, scale) > maxWidth)
                {
                    result.Add(line);
                    line = word[i].ToString();
                }
                else
                {
                    line = candidate;
                }
            }

            return line;
        }

        static float MeasureTextWidth(SurfaceScriptBase app, string text, float scale)
        {
            return app.Surface.MeasureStringInPixels(new StringBuilder(text ?? string.Empty), FONT_ID, scale).X;
        }

        static float GetLineHeight(SurfaceScriptBase app, float scale)
        {
            float height = app.Surface.MeasureStringInPixels(new StringBuilder("Ag"), FONT_ID, scale).Y;
            return height > 0f ? height : 24f * scale;
        }

        static float QuantizeQrSize(float requestedSize, float maxSize)
        {
            float safeMaxSize = Math.Max(QR_LOGICAL_SIZE, maxSize);
            float moduleSize = Math.Max(1f, (float)Math.Round(requestedSize / QR_LOGICAL_SIZE, MidpointRounding.AwayFromZero));
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
            // S() multiplies logical coordinates by QR_SPRITE_UNIT_SCALE * _scale,
            // so this makes that multiplier exactly the integer module size chosen above.
            return Math.Max(1f, qrSize / QR_LOGICAL_SIZE) / QR_SPRITE_UNIT_SCALE;
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

        void DrawQr(Color w, Color b)
        {
            S(0f, 0f, QR_LOGICAL_SIZE, QR_LOGICAL_SIZE, w);
            S(0f, 0f, QR_INNER_LOGICAL_SIZE, QR_INNER_LOGICAL_SIZE, b);
            S(18f, -18f, 10f, 10f, w);
            S(-18f, -18f, 10f, 10f, w);
            S(-18f, 18f, 10f, 10f, w);
            S(18f, -18f, 6f, 6f, b);
            S(-18f, -18f, 6f, 6f, b);
            S(-18f, 18f, 6f, 6f, b);
            S(17f, -10f, 16f, 2f, w);
            S(-17f, -10f, 16f, 2f, w);
            S(-17f, 10f, 16f, 2f, w);
            S(10f, -17f, 2f, 16f, w);
            S(-10f, -17f, 2f, 16f, w);
            S(-4f, 14f, 2f, 2f, w);
            S(-10f, 17f, 2f, 16f, w);
            S(-6f, 17f, 2f, 4f, w);
            S(-3f, 21f, 4f, 8f, w);
            S(7f, 20f, 4f, 2f, w);
            S(1f, -9f, 4f, 4f, w);
            S(1f, -3f, 4f, 4f, w);
            S(22f, -8f, 2f, 2f, w);
            S(-8f, 22f, 2f, 2f, w);
            S(24f, -7f, 2f, 4f, w);
            S(24f, 10f, 2f, 2f, w);
            S(-2f, 5f, 2f, 8f, w);
            S(9f, -2f, 4f, 2f, w);
            S(14f, 1f, 2f, 4f, w);
            S(24f, -1f, 2f, 4f, w);
            S(18f, 0f, 2f, 2f, w);
            S(-8f, 12f, 2f, 2f, w);
            S(19f, -4f, 4f, 6f, w);
            S(16f, -6f, 2f, 2f, w);
            S(12f, -6f, 2f, 2f, w);
            S(10f, -7f, 2f, 4f, w);
            S(6f, -11f, 2f, 4f, w);
            S(6f, -20f, 2f, 2f, w);
            S(8f, -18f, 2f, 2f, w);
            S(0f, 20f, 2f, 2f, w);
            S(4f, -16f, 6f, 2f, w);
            S(4f, -14f, 2f, 2f, w);
            S(2f, -12f, 2f, 2f, w);
            S(-2f, -12f, 2f, 2f, w);
            S(0f, -14f, 2f, 2f, w);
            S(4f, -4f, 2f, 2f, w);
            S(2f, -6f, 2f, 2f, w);
            S(2f, 24f, 2f, 2f, w);
            S(10f, 4f, 2f, 2f, w);
            S(6f, 2f, 6f, 2f, w);
            S(3f, 0f, 8f, 2f, w);
            S(5f, 6f, 4f, 2f, w);
            S(23f, 2f, 4f, 2f, w);
            S(5f, 22f, 4f, 2f, w);
            S(4f, 12f, 6f, 2f, w);
            S(18f, 4f, 6f, 6f, w);
            S(12f, 12f, 6f, 6f, w);
            S(18f, 4f, 2f, 2f, b);
            S(12f, 12f, 2f, 2f, b);
            S(2f, 16f, 6f, 2f, w);
            S(20f, 22f, 6f, 2f, w);
            S(14f, 24f, 6f, 2f, w);
            S(-20f, 1f, 2f, 8f, w);
            S(-24f, -3f, 2f, 8f, w);
            S(-16f, 4f, 2f, 6f, w);
            S(-8f, 5f, 2f, 4f, w);
            S(-12f, 6f, 2f, 2f, w);
            S(-10f, 8f, 2f, 2f, w);
            S(-14f, 8f, 2f, 2f, w);
            S(-18f, 7f, 2f, 4f, w);
            S(-22f, 6f, 2f, 6f, w);
            S(18f, 18f, 2f, 2f, w);
            S(14f, 18f, 2f, 2f, w);
            S(14f, 22f, 2f, 2f, w);
            S(16f, 20f, 2f, 2f, w);
            S(12f, 20f, 2f, 2f, w);
            S(8f, 18f, 2f, 2f, w);
            S(8f, 24f, 2f, 2f, w);
            S(-12f, -6f, 2f, 2f, w);
            S(19f, 11f, 4f, 4f, w);
            S(23f, 8f, 4f, 2f, w);
            S(21f, 14f, 4f, 2f, w);
            S(-17f, -6f, 4f, 2f, w);
            S(-9f, -8f, 4f, 2f, w);
            S(1f, 14f, 4f, 2f, w);
            S(-15f, 2f, 8f, 2f, w);
            S(-14f, -2f, 6f, 2f, w);
            S(-4f, -23f, 2f, 4f, w);
            S(-8f, -23f, 2f, 4f, w);
            S(-6f, -15f, 2f, 8f, w);
            S(-6f, -4f, 2f, 6f, w);
            S(-8f, -4f, 2f, 2f, w);
            S(-2f, -5f, 2f, 4f, w);
            S(-4f, -9f, 2f, 4f, w);
            S(0f, 4f, 2f, 2f, w);
            S(2f, 8f, 2f, 2f, w);
            S(-6f, 8f, 2f, 2f, w);
            S(-8f, 0f, 2f, 2f, w);
            S(0f, -19f, 2f, 4f, w);
            S(0f, 10f, 14f, 2f, w);
            S(3f, -21f, 4f, 4f, w);
            S(-3f, -17f, 4f, 4f, w);
            S(-2f, -22f, 2f, 2f, w);
            S(-10f, 4f, 2f, 2f, w);
            S(-14f, 4f, 2f, 2f, w);
            S(-14f, -4f, 2f, 2f, w);
            S(-18f, -4f, 2f, 2f, w);
            S(-22f, -2f, 2f, 2f, w);
            S(6f, 14f, 2f, 2f, w);
            S(22f, 20f, 2f, 2f, w);
            S(-22f, -8f, 2f, 2f, w);
        }

        void S(float positionX, float positionY, float sizeX, float sizeY, Color color)
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

        public static BSoD ShowBSoD(SurfaceScriptBase surfaceScriptBase, Exception exception) => new BSoD(surfaceScriptBase, exception);
    }
}
