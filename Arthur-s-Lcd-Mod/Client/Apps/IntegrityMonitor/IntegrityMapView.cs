using System;
using Acrux.Core;
using Acrux.Core.Invalidation;
using Acrux.Core.PropertySystem;
using Acrux.Rendering;
using VRageMath;

namespace LcdMod.Client.Apps.IntegrityMonitor
{
    /// <summary>
    /// Draws the integrity grid as one generated texture, centered, square-fitted, scaled and rotated. The texture is
    /// repainted only when <see cref="Version"/> changes.
    /// </summary>
    internal sealed class IntegrityMapView : VisualElement
    {
        public static readonly StyledProperty<Color?[,]> CellsProperty =
            StyledProperty.Register<IntegrityMapView, Color?[,]>(nameof(Cells), null, UiInvalidation.Render);
        public static readonly StyledProperty<int> VersionProperty =
            StyledProperty.Register<IntegrityMapView, int>(nameof(Version), 0, UiInvalidation.Render);
        public static readonly StyledProperty<float> MapRotationProperty =
            StyledProperty.Register<IntegrityMapView, float>(nameof(MapRotation), 0f, UiInvalidation.Render);
        public static readonly StyledProperty<float> MapScaleProperty =
            StyledProperty.Register<IntegrityMapView, float>(nameof(MapScale), 1f, UiInvalidation.Render);
        public static readonly StyledProperty<int> FitSideProperty =
            StyledProperty.Register<IntegrityMapView, int>(nameof(FitSide), 1, UiInvalidation.Render);

        IDynamicTexture _texture;
        IDynamicTextureFactory _factory;
        int _paintedVersion = -1;

        public Color?[,] Cells
        {
            get { return GetValue(CellsProperty); }
            set { SetValue(CellsProperty, value); }
        }

        public int Version
        {
            get { return GetValue(VersionProperty); }
            set { SetValue(VersionProperty, value); }
        }

        public float MapRotation
        {
            get { return GetValue(MapRotationProperty); }
            set { SetValue(MapRotationProperty, value); }
        }

        public float MapScale
        {
            get { return GetValue(MapScaleProperty); }
            set { SetValue(MapScaleProperty, value); }
        }

        // Grid cells along the longer side before any rotation; keeps the map size stable while it turns.
        public int FitSide
        {
            get { return GetValue(FitSideProperty); }
            set { SetValue(FitSideProperty, value); }
        }

        protected override void DrawSelf(RenderContext context)
        {
            Color?[,] cells = Cells;
            if (cells == null || Bounds.Width <= 0f || Bounds.Height <= 0f)
                return;
            int width = cells.GetLength(0);
            int height = cells.GetLength(1);
            if (width <= 0 || height <= 0)
                return;

            int side = Math.Max(1, FitSide);
            float cellSize = Math.Min(Bounds.Width / side, Bounds.Height / side) * Math.Max(.05f, MapScale);
            if (!EnsureTexture(context, cells, width, height, cellSize))
                return;
            var size = new Vector2(width * cellSize, height * cellSize);
            context.DrawTexture(_texture.Source, new RectangleF(Bounds.Center - size * .5f, size), Color.White, MapRotation);
        }

        bool EnsureTexture(RenderContext context, Color?[,] cells, int width, int height, float cellSize)
        {
            IDynamicTextureFactory factory = context.DynamicTextures;
            if (factory == null)
                return false;
            int upscale = Math.Max(1, (int)Math.Ceiling(cellSize));
            Vector2I size = factory.FitToSupportedSize(new Vector2I(width * upscale, height * upscale));
            if (size.X <= 0 || size.Y <= 0)
                return false;
            bool created = false;
            if (_texture == null || !_texture.IsAvailable || !ReferenceEquals(_factory, factory) || _texture.Size != size)
            {
                Release();
                _texture = factory.Create("IntegrityMonitor", size);
                _factory = factory;
                _paintedVersion = -1;
                created = true;
            }
            if (_texture == null || !_texture.IsAvailable)
                return false;
            if (_paintedVersion != Version)
            {
                _texture.Update(canvas => Paint(canvas, cells, width, height));
                _paintedVersion = Version;
            }
            if (!created)
                return true;
            context.MarkVolatile();
            return false;
        }

        static void Paint(PixelCanvas canvas, Color?[,] cells, int width, int height)
        {
            canvas.Clear(Color.Transparent);
            for (int y = 0; y < canvas.Height; y++)
            {
                int cellY = height - 1 - y * height / canvas.Height;
                for (int x = 0; x < canvas.Width; x++)
                {
                    Color? color = cells[x * width / canvas.Width, cellY];
                    if (color.HasValue)
                        canvas.SetPixel(x, y, color.Value);
                }
            }
        }

        void Release()
        {
            if (_texture != null)
                _texture.Dispose();
            _texture = null;
        }

        public override void Dispose()
        {
            Release();
            base.Dispose();
        }
    }
}
