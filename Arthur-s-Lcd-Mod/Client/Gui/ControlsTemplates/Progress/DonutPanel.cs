using System;
using System.Collections.Generic;
using System.Text;
using LcdMod.Client.Helpers;
using VRage.Game.GUI.TextPanel;
using VRageMath;
using IMyTextSurface = Sandbox.ModAPI.Ingame.IMyTextSurface;

namespace LcdMod.Client.Gui.ControlsTemplates.Progress
{
    public struct DonutSegment
    {
        public DonutSegment(float fraction, Color color)
        {
            Fraction = fraction;
            Color = color;
        }

        public float Fraction;
        public Color Color;
    }

    public static class DonutPanel
    {
        public static void DrawDonut(
            List<MySprite> sprites,
            Vector2 center,
            float innerRadius,
            float outerRadius,
            float fraction,
            Color fillColor,
            Color backgroundColor,
            int steps = 48,
            float startAngle = 0f,
            float gapPixels = 0f,
            bool drawBackground = true)
        {
            if (sprites == null || outerRadius <= 0f)
                return;

            innerRadius = MathHelper.Clamp(innerRadius, 0f, outerRadius);
            float thickness = outerRadius - innerRadius;
            if (thickness <= 0f)
                return;

            steps = Math.Max(8, steps);
            fraction = MathHelper.Clamp(fraction, 0f, 1f);
            float actualStep = MathHelper.TwoPi / steps;
            float midRadius = (innerRadius + outerRadius) * 0.5f;
            float segmentWidth = Math.Max(1f, 2f * outerRadius * (float)Math.Sin(actualStep * 0.5f) + 0.5f);
            int filledSteps = Math.Min(steps, Math.Max(0, (int)Math.Floor(fraction * steps + 0.5f)));
            bool drawRoundedCaps = filledSteps > 0 && filledSteps < steps && gapPixels > 0f;
            int capInsetSteps = 0;
            if (drawRoundedCaps)
            {
                float capInsetAngle = (thickness + gapPixels) / (2f * midRadius);
                int requestedInset = Math.Max(0,
                    (int)Math.Floor(capInsetAngle / actualStep + 0.5f));
                capInsetSteps = Math.Min(requestedInset, Math.Max(0, (filledSteps - 1) / 2));
            }
            int firstFilledStep = capInsetSteps;
            int filledEndStep = filledSteps - capInsetSteps;

            for (int i = 0; i < steps; i++)
            {
                float theta = startAngle + (i + 0.5f) * actualStep;
                Vector2 direction = new Vector2((float)Math.Sin(theta), -(float)Math.Cos(theta));
                Vector2 position = center + direction * midRadius;
                if (drawBackground)
                    DrawDonutRectangle(
                        sprites, position, segmentWidth, thickness, theta, backgroundColor);

                bool isFilled = i >= firstFilledStep && i < filledEndStep;
                if (isFilled)
                    DrawDonutRectangle(
                        sprites, position, segmentWidth, thickness, theta, fillColor);
            }

            if (!drawRoundedCaps)
                return;

            float firstRectangleAngle = startAngle + (firstFilledStep + 0.5f) * actualStep;
            float lastRectangleAngle = startAngle + (filledEndStep - 0.5f) * actualStep;

            DrawDonutCap(
                sprites,
                center,
                midRadius,
                segmentWidth,
                thickness,
                firstRectangleAngle,
                fillColor,
                false);
            DrawDonutCap(
                sprites,
                center,
                midRadius,
                segmentWidth,
                thickness,
                lastRectangleAngle,
                fillColor,
                true);
        }

        public static void DrawSegmentedDonut(
            List<MySprite> sprites,
            Vector2 center,
            float innerRadius,
            float outerRadius,
            IReadOnlyList<DonutSegment> segments,
            Color backgroundColor,
            int steps = 48,
            float startAngle = 0f,
            float gapPixels = 0f,
            Color? gapColor = null)
        {
            if (sprites == null || segments == null || outerRadius <= 0f)
                return;

            innerRadius = MathHelper.Clamp(innerRadius, 0f, outerRadius);
            float thickness = outerRadius - innerRadius;
            if (thickness <= 0f)
                return;

            steps = Math.Max(8, steps);
            float actualStep = MathHelper.TwoPi / steps;
            float midRadius = (innerRadius + outerRadius) * 0.5f;
            float segmentWidth = Math.Max(1f,
                2f * outerRadius * (float)Math.Sin(actualStep * 0.5f) + 0.5f);
            var endSteps = new int[segments.Count];
            float cumulativeFraction = 0f;

            for (int i = 0; i < segments.Count; i++)
            {
                cumulativeFraction = MathHelper.Clamp(
                    cumulativeFraction + Math.Max(0f, segments[i].Fraction),
                    0f,
                    1f);
                endSteps[i] = Math.Min(steps,
                    Math.Max(0, (int)Math.Floor(cumulativeFraction * steps + 0.5f)));
            }

            for (int i = 0; i < steps; i++)
            {
                Color color = backgroundColor;
                int startStep = 0;
                for (int segmentIndex = 0; segmentIndex < segments.Count; segmentIndex++)
                {
                    int endStep = endSteps[segmentIndex];
                    if (i >= startStep && i < endStep)
                    {
                        color = segments[segmentIndex].Color;
                        break;
                    }

                    startStep = endStep;
                }

                float theta = startAngle + (i + 0.5f) * actualStep;
                Vector2 direction = new Vector2((float)Math.Sin(theta), -(float)Math.Cos(theta));
                MySprite sprite = MySprite.CreateSprite(
                    "SquareSimple",
                    center + direction * midRadius,
                    new Vector2(segmentWidth, thickness));
                sprite.Color = color;
                sprite.RotationOrScale = theta;
                sprites.Add(sprite);
            }

            if (segments.Count == 0 || gapPixels <= 0f || !gapColor.HasValue)
                return;

            int filledSteps = endSteps[endSteps.Length - 1];
            if (filledSteps <= 0)
                return;

            int visibleSegments = 0;
            int previousEnd = 0;
            for (int i = 0; i < endSteps.Length; i++)
            {
                if (endSteps[i] > previousEnd)
                    visibleSegments++;
                previousEnd = endSteps[i];
            }

            if (filledSteps < steps || visibleSegments > 1)
                DrawDonutSeparator(
                    sprites, center, midRadius, thickness, startAngle, gapPixels, gapColor.Value);

            previousEnd = 0;
            for (int i = 0; i < endSteps.Length; i++)
            {
                int endStep = endSteps[i];
                if (endStep > previousEnd && endStep < filledSteps)
                    DrawDonutSeparator(
                        sprites,
                        center,
                        midRadius,
                        thickness,
                        startAngle + actualStep * endStep,
                        gapPixels,
                        gapColor.Value);
                previousEnd = endStep;
            }

            if (filledSteps < steps)
                DrawDonutSeparator(
                    sprites,
                    center,
                    midRadius,
                    thickness,
                    startAngle + actualStep * filledSteps,
                    gapPixels,
                    gapColor.Value);
        }

        static void DrawDonutSeparator(
            List<MySprite> sprites,
            Vector2 center,
            float midRadius,
            float thickness,
            float theta,
            float gapPixels,
            Color color)
        {
            Vector2 direction = new Vector2((float)Math.Sin(theta), -(float)Math.Cos(theta));
            MySprite separator = MySprite.CreateSprite(
                "SquareSimple",
                center + direction * midRadius,
                new Vector2(gapPixels, thickness + 1f));
            separator.Color = color;
            separator.RotationOrScale = theta;
            sprites.Add(separator);
        }

        static void DrawDonutRectangle(
            List<MySprite> sprites,
            Vector2 position,
            float width,
            float thickness,
            float theta,
            Color color)
        {
            MySprite sprite = MySprite.CreateSprite(
                "SquareSimple",
                position,
                new Vector2(width, thickness));
            sprite.Color = color;
            sprite.RotationOrScale = theta;
            sprites.Add(sprite);
        }

        static void DrawDonutCap(
            List<MySprite> sprites,
            Vector2 center,
            float midRadius,
            float rectangleWidth,
            float thickness,
            float rectangleTheta,
            Color color,
            bool pointsForward)
        {
            Vector2 radial = new Vector2(
                (float)Math.Sin(rectangleTheta),
                -(float)Math.Cos(rectangleTheta));
            Vector2 tangent = new Vector2(
                (float)Math.Cos(rectangleTheta),
                (float)Math.Sin(rectangleTheta));
            float edgeOffset = pointsForward ? rectangleWidth * 0.5f : -rectangleWidth * 0.5f;
            float attachmentTheta = rectangleTheta + (float)Math.Atan2(edgeOffset, midRadius);
            float capOverlap = 0.5f * Math.Abs(edgeOffset);
            float positionOffset = edgeOffset + (pointsForward ? -capOverlap : capOverlap);
            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXTURE,
                Data = "SemiCircle",
                Position = center + radial * midRadius + tangent * positionOffset,
                Size = new Vector2(thickness),
                Color = color,
                Alignment = TextAlignment.CENTER,
                RotationOrScale = attachmentTheta +
                                  (pointsForward ? MathHelper.PiOver2 : -MathHelper.PiOver2)
            });
        }

        public static void DrawCenterPercentage(
            List<MySprite> sprites,
            IMyTextSurface surface,
            Vector2 center,
            float innerRadius,
            float value,
            Color color,
            string fontId,
            float requestedScale)
        {
            if (sprites == null || surface == null || innerRadius <= 0f || requestedScale <= 0f)
                return;

            string text = FormatingHelper.PercentageToString(MathHelper.Clamp(value, 0f, 1f));
            string resolvedFont = string.IsNullOrEmpty(fontId) ? "White" : fontId;
            float availableSize = innerRadius * 1.6f;
            var textBuilder = new StringBuilder(text);
            Vector2 measured = surface.MeasureStringInPixels(textBuilder, resolvedFont, requestedScale);
            if (measured.X <= 0f || measured.Y <= 0f)
                return;

            float fit = Math.Min(1f, Math.Min(availableSize / measured.X, availableSize / measured.Y));
            float scale = Math.Max(0.01f, requestedScale * fit);
            measured *= scale / requestedScale;
            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT,
                Data = text,
                Position = new Vector2(center.X, center.Y - measured.Y * 0.5f),
                RotationOrScale = scale,
                Color = color,
                Alignment = TextAlignment.CENTER,
                FontId = resolvedFont
            });
        }

        public static void CreateSprites(
            List<MySprite> sprites,
            string title,
            IMyTextSurface surface,
            Vector2 margin,
            Vector2 size,
            float value,
            Color? color = null,
            bool turnDarkOnComplete = false,
            bool showTitle = true)
        {
            if (color == null)
                color = surface.ScriptForegroundColor;

            var origo = new Vector2(margin.X, 512 - margin.Y);
            var backgroundColor = surface.ScriptForegroundColor;

            if (showTitle) DrawTitle(sprites, title, origo, size, value, color.Value);
            float outerRadius = Math.Max(0f, Math.Min(size.X, size.Y) * 0.5f);
            DrawDonut(
                sprites,
                origo,
                outerRadius * 0.68f,
                outerRadius,
                value,
                color.Value,
                GetDarkenedColor(backgroundColor),
                gapPixels: 2f);
        }

        internal static Color GetDarkenedColor(Color color) =>
            new Color((int)(color.R * 0.5f), (int)(color.G * 0.5f), (int)(color.B * 0.5f), color.A);

        internal static void DrawTitle(
            List<MySprite> sprites,
            string title,
            Vector2 origo,
            Vector2 size,
            float value,
            Color color)
        {
            Vector2 titleSize = new Vector2(size.X, 18);
            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXTURE,
                Data = "SquareSimple",
                Position = origo - new Vector2(size.X, size.Y + (titleSize.Y / 2) + 10),
                Size = new Vector2(size.X * 2, titleSize.Y),
                Color = new Color(0, 0, 0, 140),
                Alignment = TextAlignment.LEFT
            });

            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT,
                Data = (title ?? string.Empty) + FormatingHelper.PercentageToString(value),
                Position = origo - new Vector2(size.X - 4, size.Y + (titleSize.Y / 2) + 16),
                Color = color,
                Alignment = TextAlignment.LEFT,
                RotationOrScale = 0.55f
            });
        }
    }
}
