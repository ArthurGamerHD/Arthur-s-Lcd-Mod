using System;
using System.Collections.Generic;
using LcdMod.Client.Helpers;
using LcdMod.Client.SurfaceScripts.Abstract;
using LcdMod.Common.Helpers;
using VRage.Collections;
using VRage.Game.GUI.TextPanel;
using VRageMath;
using static LcdMod.Common.Helpers.Constants;

namespace LcdMod.Client.Gui.UserControls
{
    class BSoD
    {
        const string FONT_ID = "Debug";
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
                MOD_PREFIX + "BSoD_Header", BSOD_TITLE_FALLBACK);
            var infoText = GetLocalizedText(MOD_PREFIX + "BSoD_InfoIntro", BSOD_INFO1_FALLBACK) + " " +
                           GetLocalizedText(MOD_PREFIX + "BSoD_InfoUrl", BSOD_INFO2_FALLBACK) + GITHUB + " " +
                           GetLocalizedText(MOD_PREFIX + "BSoD_InfoQr", BSOD_INFO3_FALLBACK);
            var exceptionText = 
                GetLocalizedText(MOD_PREFIX + "BSoD_SupportIntro", BSOD_INFO4_FALLBACK) +
                "\n" +
                GetLocalizedText(MOD_PREFIX + "BSoD_ExceptionCode", BSOD_INFO5_FALLBACK) + "\n" + exception;

            float layoutScale = Math.Max(0.45f, Math.Min(viewBox.Width, viewBox.Height) / 512f);
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
            float cursorY = viewTop;

            string sadFaceTitle = ":(";
            float titleTextHeight = Math.Max(MeasureText(sadFaceTitle, sadFace).Y,
                TextWrappingHelper.GetLineHeight(_surface, FONT_ID, sadFace, 0f));
            float titleRowHeight = titleTextHeight;
            float titleTextY = cursorY + (titleRowHeight - titleTextHeight) * 0.5f;
            float titleTextX = viewLeft;

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

            float availableWidth = Math.Max(8f, viewBox.Width);
            float headerHeight = Math.Max(8f, viewBottom - cursorY);
            var headerLines = TextWrappingHelper.WrapText(
                headerText,
                _surface,
                FONT_ID,
                headerScale,
                availableWidth,
                headerHeight,
                2f,
                false);
            DrawMultilineText(headerLines, viewLeft, cursorY, headerScale, white);
            cursorY += GetLinesHeight(headerLines, headerScale) + sectionGap;

            float remainingHeightForContent = Math.Max(52f, viewBottom - cursorY);
            float requestedQrSize = Math.Min(availableWidth * 0.24f, remainingHeightForContent * 0.42f);
            float quantizedQr = QuantizeQrSize(requestedQrSize, requestedQrSize);
            float qrSize = RoundToPixel(Math.Max(40f * layoutScale, Math.Min(requestedQrSize, quantizedQr)));
            var qrRect = CreateIntegerQrRect(
                viewLeft,
                cursorY,
                qrSize);

            _scale = GetQrScale(qrRect.Width);
            _centerPos = qrRect.Center;
            DrawQr(white, blue);

            float infoX = qrRect.Right + contentGap;
            float infoWidth = Math.Max(8f, viewRight - infoX);
            float infoHeight = qrRect.Height;

            var infoLines = TextWrappingHelper.WrapText(
                infoText,
                _surface,
                FONT_ID,
                infoScale,
                infoWidth,
                infoHeight,
                2f,
                false);
            DrawMultilineText(infoLines, infoX, qrRect.Y, infoScale, white);

            cursorY = qrRect.Bottom + sectionGap;

            float exceptionHeight = Math.Max(8f, viewBottom - cursorY);
            var exceptionLines = TextWrappingHelper.WrapText(
                exceptionText,
                _surface,
                FONT_ID,
                exceptionScale,
                availableWidth,
                exceptionHeight,
                2f,
                true);
            DrawMultilineText(exceptionLines, viewLeft, cursorY, exceptionScale, white);

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

        Vector2 MeasureText(string text, float scale)
        {
            return FormatingHelper.GetSizeInPixel(text, FONT_ID, scale, _surface);
        }

        void DrawMultilineText(List<string> lines, float x, float y, float scale, Color color)
        {
            if (lines == null || lines.Count == 0)
                return;

            float lineHeight = TextWrappingHelper.GetLineHeight(_surface, FONT_ID, scale, 2f);
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
            float lineHeight = TextWrappingHelper.GetLineHeight(_surface, FONT_ID, scale, 2f);
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
