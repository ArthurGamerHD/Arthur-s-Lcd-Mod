using System;
using LcdMod.Common.Helpers;
using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI;
using IMyTextSurface = Sandbox.ModAPI.Ingame.IMyTextSurface;
using IMyTextSurfaceProvider = Sandbox.ModAPI.Ingame.IMyTextSurfaceProvider;

namespace LcdMod.Client.Helpers
{
    public static class SurfaceAspectRatioHelper
    {
        public static bool CanShowTitle(IMyTerminalBlock block)
        {
            return MeetsMinimumHeightToWidthRatio(block, Constants.MIN_SCREEN_HEIGHT_TO_WIDTH_RATIO);
        }

        public static bool CanShowTitle(IMyTextSurface surface)
        {
            return MeetsMinimumHeightToWidthRatio(surface, Constants.MIN_SCREEN_HEIGHT_TO_WIDTH_RATIO);
        }

        public static bool MeetsMinimumHeightToWidthRatio(IMyTerminalBlock block, float minimumHeightToWidthRatio)
        {
            if (!HasMinimumHeightToWidthRatio(minimumHeightToWidthRatio))
                return true;
            if (block == null)
                return false;

            var provider = block as IMyTextSurfaceProvider;
            if (provider == null || provider.SurfaceCount <= 0)
                return false;

            var multiTextPanel = block.Components.Get<MyMultiTextPanelComponent>();
            int surfaceIndex = multiTextPanel == null ? 0 : multiTextPanel.SelectedPanelIndex;
            if (surfaceIndex < 0 || surfaceIndex >= provider.SurfaceCount)
                surfaceIndex = 0;

            return MeetsMinimumHeightToWidthRatio(provider.GetSurface(surfaceIndex), minimumHeightToWidthRatio);
        }

        public static bool MeetsMinimumHeightToWidthRatio(IMyTextSurface surface, float minimumHeightToWidthRatio)
        {
            if (!HasMinimumHeightToWidthRatio(minimumHeightToWidthRatio))
                return true;
            if (surface == null)
                return false;

            var surfaceSize = surface.SurfaceSize;
            return MeetsMinimumHeightToWidthRatio(surfaceSize.X, surfaceSize.Y, minimumHeightToWidthRatio);
        }

        public static bool MeetsMinimumHeightToWidthRatio(
            float width,
            float height,
            float minimumHeightToWidthRatio)
        {
            if (!HasMinimumHeightToWidthRatio(minimumHeightToWidthRatio))
                return true;
            if (!IsFinitePositive(width) || !IsFinitePositive(height))
                return false;

            return height / Math.Max(1f, width) >= minimumHeightToWidthRatio;
        }

        static bool HasMinimumHeightToWidthRatio(float minimumHeightToWidthRatio)
        {
            return !float.IsNaN(minimumHeightToWidthRatio) &&
                   !float.IsInfinity(minimumHeightToWidthRatio) &&
                   minimumHeightToWidthRatio > 0f;
        }

        static bool IsFinitePositive(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value) && value > 0f;
        }
    }
}
