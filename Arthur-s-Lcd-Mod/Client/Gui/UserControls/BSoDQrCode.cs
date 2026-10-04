using System;
using Adk.Gui.Core;
using Adk.Gui.Rendering;
using VRageMath;

namespace LcdMod.Client.Gui.UserControls
{
    /// <summary>
    /// The crash screen's support QR code, drawn from one generated texture shared for the session. SE skips a
    /// generated texture in the frame it is created, so callers re-upload a frame that showed it for the first time.
    /// </summary>
    internal sealed class BSoDQrCode : VisualElement
    {
        const string TextureName = "LcdMod_BSoD_QR";
        const int Modules = 52;
        const int PixelsPerModule = 8;
        static readonly Color Light = new Color(179, 237, 255);
        static readonly Color Dark = new Color(0, 88, 151);

        // Rectangles in modules: center x, center y, width, height, light (1) or dark (0), relative to the code center.
        static readonly int[] Rectangles =
        {
            0, 0, 52, 52, 1, 0, 0, 50, 50, 0, 18, -18, 10, 10, 1, -18, -18, 10, 10, 1, -18, 18, 10, 10, 1, 18, -18, 6, 6, 0,
            -18, -18, 6, 6, 0, -18, 18, 6, 6, 0, 17, -10, 16, 2, 1, -17, -10, 16, 2, 1, -17, 10, 16, 2, 1, 10, -17, 2, 16, 1,
            -10, -17, 2, 16, 1, -4, 14, 2, 2, 1, -10, 17, 2, 16, 1, -6, 17, 2, 4, 1, -3, 21, 4, 8, 1, 7, 20, 4, 2, 1,
            1, -9, 4, 4, 1, 1, -3, 4, 4, 1, 22, -8, 2, 2, 1, -8, 22, 2, 2, 1, 24, -7, 2, 4, 1, 24, 10, 2, 2, 1,
            -2, 5, 2, 8, 1, 9, -2, 4, 2, 1, 14, 1, 2, 4, 1, 24, -1, 2, 4, 1, 18, 0, 2, 2, 1, -8, 12, 2, 2, 1,
            19, -4, 4, 6, 1, 16, -6, 2, 2, 1, 12, -6, 2, 2, 1, 10, -7, 2, 4, 1, 6, -11, 2, 4, 1, 6, -20, 2, 2, 1,
            8, -18, 2, 2, 1, 0, 20, 2, 2, 1, 4, -16, 6, 2, 1, 4, -14, 2, 2, 1, 2, -12, 2, 2, 1, -2, -12, 2, 2, 1,
            0, -14, 2, 2, 1, 4, -4, 2, 2, 1, 2, -6, 2, 2, 1, 2, 24, 2, 2, 1, 10, 4, 2, 2, 1, 6, 2, 6, 2, 1,
            3, 0, 8, 2, 1, 5, 6, 4, 2, 1, 23, 2, 4, 2, 1, 5, 22, 4, 2, 1, 4, 12, 6, 2, 1, 18, 4, 6, 6, 1,
            12, 12, 6, 6, 1, 18, 4, 2, 2, 0, 12, 12, 2, 2, 0, 2, 16, 6, 2, 1, 20, 22, 6, 2, 1, 14, 24, 6, 2, 1,
            -20, 1, 2, 8, 1, -24, -3, 2, 8, 1, -16, 4, 2, 6, 1, -8, 5, 2, 4, 1, -12, 6, 2, 2, 1, -10, 8, 2, 2, 1,
            -14, 8, 2, 2, 1, -18, 7, 2, 4, 1, -22, 6, 2, 6, 1, 18, 18, 2, 2, 1, 14, 18, 2, 2, 1, 14, 22, 2, 2, 1,
            16, 20, 2, 2, 1, 12, 20, 2, 2, 1, 8, 18, 2, 2, 1, 8, 24, 2, 2, 1, -12, -6, 2, 2, 1, 19, 11, 4, 4, 1,
            23, 8, 4, 2, 1, 21, 14, 4, 2, 1, -17, -6, 4, 2, 1, -9, -8, 4, 2, 1, 1, 14, 4, 2, 1, -15, 2, 8, 2, 1,
            -14, -2, 6, 2, 1, -4, -23, 2, 4, 1, -8, -23, 2, 4, 1, -6, -15, 2, 8, 1, -6, -4, 2, 6, 1, -8, -4, 2, 2, 1,
            -2, -5, 2, 4, 1, -4, -9, 2, 4, 1, 0, 4, 2, 2, 1, 2, 8, 2, 2, 1, -6, 8, 2, 2, 1, -8, 0, 2, 2, 1,
            0, -19, 2, 4, 1, 0, 10, 14, 2, 1, 3, -21, 4, 4, 1, -3, -17, 4, 4, 1, -2, -22, 2, 2, 1, -10, 4, 2, 2, 1,
            -14, 4, 2, 2, 1, -14, -4, 2, 2, 1, -18, -4, 2, 2, 1, -22, -2, 2, 2, 1, 6, 14, 2, 2, 1, 22, 20, 2, 2, 1,
            -22, -8, 2, 2, 1
        };

        static IDynamicTexture _texture;

        /// <summary>True when the shared texture was created during the last draw and is not live yet.</summary>
        internal static bool CreatedThisFrame { get; private set; }

        internal static void Release()
        {
            if (_texture != null)
                _texture.Dispose();
            _texture = null;
        }

        protected override Vector2 MeasureOverride(Vector2 availableSize)
        {
            return Vector2.Zero;
        }

        protected override void DrawSelf(RenderContext context)
        {
            CreatedThisFrame = false;
            float side = Math.Min(Bounds.Width, Bounds.Height);
            if (side <= 0f || !EnsureTexture(context))
                return;
            var rect = new RectangleF(Bounds.Center - new Vector2(side * .5f), new Vector2(side));
            context.DrawTexture(_texture.Source, rect, Color.White);
        }

        static bool EnsureTexture(RenderContext context)
        {
            if (_texture != null && _texture.IsAvailable)
                return true;
            if (context.DynamicTextures == null)
                return false;
            Release();
            int size = Modules * PixelsPerModule;
            _texture = context.DynamicTextures.Create(TextureName, new Vector2I(size, size));
            if (_texture == null || !_texture.IsAvailable || !_texture.Update(Paint))
            {
                Release();
                return false;
            }
            CreatedThisFrame = true;
            return true;
        }

        static void Paint(PixelCanvas canvas)
        {
            canvas.Clear(Dark);
            float scale = canvas.Width / (float)Modules;
            for (int i = 0; i < Rectangles.Length; i += 5)
            {
                float left = (Rectangles[i] - Rectangles[i + 2] * .5f + Modules * .5f) * scale;
                float top = (Rectangles[i + 1] - Rectangles[i + 3] * .5f + Modules * .5f) * scale;
                canvas.FillRectangle((int)Math.Round(left), (int)Math.Round(top),
                    (int)Math.Round(Rectangles[i + 2] * scale), (int)Math.Round(Rectangles[i + 3] * scale),
                    Rectangles[i + 4] == 1 ? Light : Dark);
            }
        }
    }
}
