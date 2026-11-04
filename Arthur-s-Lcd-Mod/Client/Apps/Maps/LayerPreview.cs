using System;
using Acrux.Core;
using Acrux.Core.Invalidation;
using Acrux.Core.PropertySystem;
using Acrux.Rendering;
using Acrux.Cartography;
using VRageMath;

namespace LcdMod.Client.Apps.Maps
{
    /// <summary>The cubemap face under the reference position, painted into a small generated texture.</summary>
    internal sealed class LayerPreview : VisualElement
    {
        public static readonly StyledProperty<PlanetColorCubemap> CubemapProperty =
            StyledProperty.Register<LayerPreview, PlanetColorCubemap>(nameof(Cubemap), null, UiInvalidation.Render);
        public static readonly StyledProperty<PlanetCubeFace> FaceProperty =
            StyledProperty.Register<LayerPreview, PlanetCubeFace>(nameof(Face), PlanetCubeFace.Front, UiInvalidation.Render);
        public static readonly StyledProperty<Color> PlaceholderProperty =
            StyledProperty.Register<LayerPreview, Color>(nameof(Placeholder), Color.Gray, UiInvalidation.Render);

        IDynamicTexture _texture;
        IDynamicTextureFactory _factory;
        PlanetColorCubemap _painted;
        PlanetCubeFace _paintedFace;

        public PlanetColorCubemap Cubemap
        {
            get { return GetValue(CubemapProperty); }
            set { SetValue(CubemapProperty, value); }
        }

        public PlanetCubeFace Face
        {
            get { return GetValue(FaceProperty); }
            set { SetValue(FaceProperty, value); }
        }

        public Color Placeholder
        {
            get { return GetValue(PlaceholderProperty); }
            set { SetValue(PlaceholderProperty, value); }
        }

        protected override void DrawSelf(RenderContext context)
        {
            if (Bounds.Width <= 0f || Bounds.Height <= 0f)
                return;
            if (EnsureTexture(context))
                context.DrawTexture(_texture.Source, Bounds, Color.White);
            else
                context.Fill(Bounds, Placeholder);
        }

        bool EnsureTexture(RenderContext context)
        {
            PlanetColorCubemap cubemap = Cubemap;
            IDynamicTextureFactory factory = context.DynamicTextures;
            if (cubemap == null || factory == null)
                return false;
            float scaling = Math.Max(.0001f, context.RenderScaling);
            Vector2I size = factory.FitToSupportedSize(new Vector2I(
                Math.Max(1, (int)Math.Round(Bounds.Width * scaling)),
                Math.Max(1, (int)Math.Round(Bounds.Height * scaling))));
            if (size.X <= 0 || size.Y <= 0)
                return false;

            bool created = false;
            if (_texture == null || !_texture.IsAvailable || !ReferenceEquals(_factory, factory) || _texture.Size != size)
            {
                Release();
                _texture = factory.Create("CartographyLayerPreview", size);
                _factory = factory;
                _painted = null;
                created = true;
            }
            if (_texture == null || !_texture.IsAvailable)
                return false;
            PlanetCubeFace face = Face;
            if (!IsSameCubemap(_painted, cubemap) || _paintedFace != face)
            {
                _texture.Update(canvas => Paint(canvas, cubemap, face));
                _painted = cubemap;
                _paintedFace = face;
            }
            if (!created)
                return true;
            context.MarkVolatile();
            return false;
        }

        // The bridge hands out a new wrapper per read: wrapper identity alone would repaint the preview every frame.
        internal static bool IsSameCubemap(PlanetColorCubemap a, PlanetColorCubemap b)
        {
            if (ReferenceEquals(a, b))
                return true;
            return a != null && b != null && ReferenceEquals(a.Native, b.Native);
        }

        static void Paint(PixelCanvas canvas, PlanetColorCubemap cubemap, PlanetCubeFace face)
        {
            int mip = cubemap.SelectMipLevel(Math.Max(canvas.Width, canvas.Height));
            for (int y = 0; y < canvas.Height; y++)
            {
                float v = (y + .5f) / canvas.Height;
                for (int x = 0; x < canvas.Width; x++)
                    canvas.SetPixel(x, y, cubemap.SampleFace(face, (x + .5f) / canvas.Width, v, mip));
            }
        }

        void Release()
        {
            if (_texture != null)
                _texture.Dispose();
            _texture = null;
            _painted = null;
        }

        public override void Dispose()
        {
            Release();
            base.Dispose();
        }
    }
}
