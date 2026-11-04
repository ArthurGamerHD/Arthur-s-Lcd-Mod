using LcdMod.Client.SurfaceScripts.Abstract;
using VRage.ModAPI;
using VRageMath;

namespace LcdMod.Client.Gui
{
    /// <summary>
    /// A virtual LCD cursor driven by raw mouse movement, for native screens that freeze the camera (so eye tracking
    /// cannot aim). Sensitivity is relative to the surface height, so it feels the same on any panel resolution.
    /// </summary>
    internal static class SurfacePointer
    {
        const float SurfaceHeightsPerMouseCount = 1f / 1000f;

        public static void Move(InteractiveSurfaceScript surface, ref Vector2 pointer, IMyInput input)
        {
            var textSurface = surface.Surface;
            Vector2 min = (textSurface.TextureSize - textSurface.SurfaceSize) * .5f;
            Vector2 max = min + textSurface.SurfaceSize;
            if (float.IsNaN(pointer.X) || float.IsNaN(pointer.Y))
                pointer = (min + max) * .5f;
            float scale = textSurface.SurfaceSize.Y * SurfaceHeightsPerMouseCount;
            pointer = Vector2.Clamp(pointer + new Vector2(input.GetMouseX(), input.GetMouseY()) * scale, min, max);
            surface.LookAt(pointer);
        }
    }
}
