using System;

namespace LcdMod.Common.Layout
{
    public enum ButtonPanelStyle
    {
        Default = 0,
        Classic = 1,
        Transparent = 2,
        Border = 3
    }

    public struct ButtonPanelGridLayout
    {
        public int ButtonCount;
        public int Columns;
        public int Rows;
        public float ButtonSize;
        public float Spacing;
        public float CellWidth;
        public float CellHeight;

        public float HorizontalMargin => Math.Max(0f, (CellWidth - ButtonSize) * 0.5f);
        public float VerticalMargin => Math.Max(0f, (CellHeight - ButtonSize) * 0.5f);
        public float HorizontalSpacing => HorizontalMargin * 2f;
        public float VerticalSpacing => VerticalMargin * 2f;
        public float Width => Columns * CellWidth;
        public float Height => Rows * CellHeight;
    }

    public static class ButtonPanelLayout
    {
        public const int AutomaticButtonCount = -1;
        public const int DefaultButtonCount = AutomaticButtonCount;
        public const int MinimumButtonCount = 1;
        public const float MinimumButtonSizePixels = 64f;
        public const float PreferredButtonSizePixels = 92f;
        public const float SpacingPixels = 4f;

        public static int GetMaximumButtonCount(float width, float height, float spacing)
        {
            var columns = GetMaximumCells(width, spacing);
            var rows = GetMaximumCells(height, spacing);
            return Math.Max(MinimumButtonCount, columns * rows);
        }

        public static int NormalizeButtonCount(int requestedCount, float width, float height, float spacing)
        {
            var maximum = GetMaximumButtonCount(width, height, spacing);
            var requested = Math.Max(MinimumButtonCount, Math.Min(maximum, requestedCount));
            var maximumColumns = GetMaximumCells(width, spacing);
            var maximumRows = GetMaximumCells(height, spacing);
            var bestCount = MinimumButtonCount;
            var bestDistance = Math.Abs(requested - bestCount);

            for (var rows = 1; rows <= maximumRows; rows++)
            {
                for (var columns = 1; columns <= maximumColumns; columns++)
                {
                    var count = rows * columns;
                    var distance = Math.Abs(requested - count);
                    if (distance < bestDistance || distance == bestDistance && count > bestCount)
                    {
                        bestCount = count;
                        bestDistance = distance;
                    }
                }
            }

            return bestCount;
        }

        public static int FromSlider(float sliderValue, float width, float height, float spacing)
        {
            var maximum = GetMaximumButtonCount(width, height, spacing);
            if (maximum <= MinimumButtonCount)
                return MinimumButtonCount;

            var normalized = Clamp01(sliderValue);
            var requested = (int)Math.Round(Math.Exp(Math.Log(maximum) * normalized));
            return NormalizeButtonCount(requested, width, height, spacing);
        }

        public static float ToSlider(int buttonCount, float width, float height, float spacing)
        {
            var maximum = GetMaximumButtonCount(width, height, spacing);
            if (maximum <= MinimumButtonCount)
                return 0f;

            var count = NormalizeButtonCount(buttonCount, width, height, spacing);
            return Clamp01((float)(Math.Log(count) / Math.Log(maximum)));
        }

        public static ButtonPanelGridLayout Create(
            int requestedCount,
            float width,
            float height,
            float preferredButtonSize,
            float spacing,
            int minimumDisplayedButtonCount = MinimumButtonCount)
        {
            width = GetSafeLength(width);
            height = GetSafeLength(height);
            var preferred = GetPreferredButtonSize(preferredButtonSize);
            var safeSpacing = GetSafeSpacing(spacing);
            var requiredCount = Math.Max(MinimumButtonCount, minimumDisplayedButtonCount);

            if (requestedCount == AutomaticButtonCount)
            {
                var automatic = CreateAutomatic(width, height, preferred, safeSpacing);
                return automatic.ButtonCount >= requiredCount
                    ? automatic
                    : CreateMinimumGrid(requiredCount, width, height, preferred, safeSpacing);
            }

            var count = NormalizeButtonCount(requestedCount, width, height, safeSpacing);
            if (count < requiredCount)
                return CreateMinimumGrid(requiredCount, width, height, preferred, safeSpacing);

            int bestColumns;
            int bestRows;
            if (!TrySelectExactGrid(count, width, height, safeSpacing, out bestColumns, out bestRows))
            {
                bestColumns = 1;
                bestRows = count;
            }

            return CreateFittedGrid(count, bestColumns, bestRows, width, height, safeSpacing);
        }

        static bool TrySelectExactGrid(
            int count,
            float width,
            float height,
            float spacing,
            out int bestColumns,
            out int bestRows)
        {
            var maximumColumns = GetMaximumCells(width, spacing);
            var maximumRows = GetMaximumCells(height, spacing);
            bestColumns = 1;
            bestRows = count;
            var bestSize = 0f;
            var bestShapeError = float.MaxValue;
            var bestAspectError = float.MaxValue;
            var targetAspect = height > 0f ? width / height : 1f;

            for (var rows = 1; rows <= maximumRows; rows++)
            {
                if (count % rows != 0)
                    continue;

                var columns = count / rows;
                if (columns < 1 || columns > maximumColumns)
                    continue;

                var widthPerButton = width / columns - spacing;
                var heightPerButton = height / rows - spacing;
                var size = Math.Min(widthPerButton, heightPerButton);
                if (size + 0.001f < MinimumButtonSizePixels)
                    continue;

                var gridAspect = rows > 0 ? columns / (float)rows : 1f;
                var shapeError = (float)Math.Abs(Math.Log(gridAspect));
                var aspectError = (float)Math.Abs(Math.Log(gridAspect / Math.Max(0.001f, targetAspect)));
                if (shapeError < bestShapeError - 0.001f ||
                    Math.Abs(shapeError - bestShapeError) <= 0.001f &&
                    (aspectError < bestAspectError - 0.001f ||
                     Math.Abs(aspectError - bestAspectError) <= 0.001f && size > bestSize + 0.001f))
                {
                    bestColumns = columns;
                    bestRows = rows;
                    bestSize = size;
                    bestShapeError = shapeError;
                    bestAspectError = aspectError;
                }
            }

            return bestSize > 0f;
        }

        static ButtonPanelGridLayout CreateMinimumGrid(
            int requiredCount,
            float width,
            float height,
            float preferredButtonSize,
            float spacing)
        {
            var maximum = GetMaximumButtonCount(width, height, spacing);
            if (requiredCount <= maximum)
            {
                for (var count = requiredCount; count <= maximum; count++)
                {
                    int columns;
                    int rows;
                    if (TrySelectExactGrid(count, width, height, spacing, out columns, out rows))
                        return CreateFittedGrid(
                            count,
                            columns,
                            rows,
                            width,
                            height,
                            spacing);
                }
            }

            var maximumColumns = GetMaximumCells(width, spacing);
            var bestColumns = 1;
            var bestRows = requiredCount;
            var bestShapeError = float.MaxValue;
            var bestExtraCount = int.MaxValue;
            var targetAspect = height > 0f ? width / height : 1f;
            var bestAspectError = float.MaxValue;

            for (var columns = 1; columns <= maximumColumns; columns++)
            {
                var rows = (int)Math.Ceiling(requiredCount / (float)columns);
                var count = columns * rows;
                var gridAspect = columns / (float)rows;
                var shapeError = (float)Math.Abs(Math.Log(gridAspect));
                var aspectError = (float)Math.Abs(Math.Log(gridAspect / Math.Max(0.001f, targetAspect)));
                var extraCount = count - requiredCount;

                if (shapeError < bestShapeError - 0.001f ||
                    Math.Abs(shapeError - bestShapeError) <= 0.001f &&
                    (extraCount < bestExtraCount ||
                     extraCount == bestExtraCount && aspectError < bestAspectError - 0.001f))
                {
                    bestColumns = columns;
                    bestRows = rows;
                    bestShapeError = shapeError;
                    bestExtraCount = extraCount;
                    bestAspectError = aspectError;
                }
            }

            var cellWidth = width / bestColumns;
            var buttonSize = Math.Max(1f, Math.Min(preferredButtonSize, cellWidth - spacing));
            var cellHeight = buttonSize + spacing;

            return new ButtonPanelGridLayout
            {
                ButtonCount = bestColumns * bestRows,
                Columns = bestColumns,
                Rows = bestRows,
                ButtonSize = buttonSize,
                Spacing = spacing,
                CellWidth = cellWidth,
                CellHeight = cellHeight
            };
        }

        static ButtonPanelGridLayout CreateAutomatic(
            float width,
            float height,
            float preferredButtonSize,
            float spacing)
        {
            var columns = GetMaximumCells(width, spacing, preferredButtonSize);
            var rows = GetMaximumCells(height, spacing, preferredButtonSize);

            var layout = CreateFittedGrid(
                Math.Max(MinimumButtonCount, columns * rows),
                columns,
                rows,
                width,
                height,
                spacing);
            layout.ButtonSize = Math.Min(layout.ButtonSize, preferredButtonSize);
            return layout;
        }

        static ButtonPanelGridLayout CreateFittedGrid(
            int count,
            int columns,
            int rows,
            float width,
            float height,
            float spacing)
        {
            columns = Math.Max(1, columns);
            rows = Math.Max(1, rows);
            var cellWidth = width / columns;
            var cellHeight = height / rows;
            var size = Math.Max(1f, Math.Min(cellWidth - spacing, cellHeight - spacing));

            return new ButtonPanelGridLayout
            {
                ButtonCount = Math.Max(MinimumButtonCount, count),
                Columns = columns,
                Rows = rows,
                ButtonSize = size,
                Spacing = spacing,
                CellWidth = cellWidth,
                CellHeight = cellHeight
            };
        }

        static int GetMaximumCells(float length, float spacing)
        {
            return GetMaximumCells(length, spacing, MinimumButtonSizePixels);
        }

        static int GetMaximumCells(float length, float spacing, float buttonSize)
        {
            if (float.IsNaN(length) || float.IsInfinity(length) || length <= 0f)
                return 1;

            var safeSpacing = GetSafeSpacing(spacing);
            return Math.Max(1, (int)Math.Floor(
                length / (buttonSize + safeSpacing)));
        }

        static float GetSafeLength(float length)
        {
            return float.IsNaN(length) || float.IsInfinity(length) || length <= 0f
                ? 1f
                : length;
        }

        static float GetPreferredButtonSize(float preferredButtonSize)
        {
            if (float.IsNaN(preferredButtonSize) || float.IsInfinity(preferredButtonSize) ||
                preferredButtonSize <= 0f)
                preferredButtonSize = PreferredButtonSizePixels;

            return Math.Max(MinimumButtonSizePixels, preferredButtonSize);
        }

        static float GetSafeSpacing(float spacing)
        {
            return float.IsNaN(spacing) || float.IsInfinity(spacing)
                ? 0f
                : Math.Max(0f, spacing);
        }

        static float Clamp01(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
                return 0f;
            return Math.Max(0f, Math.Min(1f, value));
        }
    }
}
