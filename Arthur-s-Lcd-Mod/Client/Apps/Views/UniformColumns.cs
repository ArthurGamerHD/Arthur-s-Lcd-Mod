using System;

namespace LcdMod.Client.Apps.Views
{
    internal static class UniformColumns
    {
        public static int Calculate(float availableWidth, float minimumColumnWidth, int itemCount)
        {
            int columns = Math.Max(1, (int)Math.Floor(availableWidth / Math.Max(1f, minimumColumnWidth)));
            if (itemCount > 0)
                columns = Math.Min(columns, itemCount);
            return columns;
        }
    }
}
