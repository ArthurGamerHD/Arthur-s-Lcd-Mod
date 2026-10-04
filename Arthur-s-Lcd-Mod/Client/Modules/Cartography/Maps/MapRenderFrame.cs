using System;
using System.Collections.Generic;
using Adk.Gui.Rendering;
using VRageMath;

namespace LcdMod.Client.Modules.Cartography.Maps
{
    /// <summary>
    /// Captured raster work. Commands read only captured values and immutable cubemaps;
    /// each worker owns a disjoint range of rows, including the matching depth values.
    /// </summary>
    internal sealed class MapRenderFrame
    {
        public readonly List<Action<PixelCanvas, int, int>> Commands =
            new List<Action<PixelCanvas, int, int>>();
        public Action Initialize;
        public Action<PixelCanvas> Finish;
        public float[] DepthBuffer;

        public void Paint(PixelCanvas canvas, Action<int, int, Action<int>> parallelFor)
        {
            canvas.Clear(Color.Transparent);
            if (Initialize != null)
                Initialize();

            // More bands than workers lets the game scheduler balance globe limbs
            // against the more expensive centre without creating threads per frame.
            const int rowsPerBand = 16;
            int bands = (canvas.Height + rowsPerBand - 1) / rowsPerBand;
            Exception failure = null;
            parallelFor(0, bands, band =>
            {
                try
                {
                    int first = band * rowsPerBand;
                    int end = Math.Min(canvas.Height, first + rowsPerBand);
                    for (int i = 0; i < Commands.Count; i++)
                        Commands[i](canvas, first, end);
                }
                catch (Exception exception)
                {
                    // Catch inside the worker: the engine may be configured to
                    // treat uncaught parallel task exceptions as fatal.
                    lock (Commands)
                        failure = exception;
                }
            });
            if (failure != null)
                throw new Exception("Map rasterization failed.", failure);

            // Orbit coverage merges intersecting strokes and must follow every
            // planet's depth writes. It still runs on the background coordinator.
            if (Finish != null)
                Finish(canvas);
        }
    }
}
