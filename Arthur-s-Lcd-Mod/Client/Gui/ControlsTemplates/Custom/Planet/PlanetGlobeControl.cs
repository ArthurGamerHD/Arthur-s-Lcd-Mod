using System;
using System.Collections.Generic;
using LcdMod.Client.Modules.Cartography;
using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace LcdMod.Client.Gui.ControlsTemplates.Custom.Planet
{
    /// <summary>
    /// Renders a mipmapped planet cubemap as an orthographic sphere using native
    /// SquareSimple sprites. View and screen-axis directions are expressed in the
    /// same planet-local coordinate frame used by PlanetColorCubemap.
    /// </summary>
    public sealed class PlanetGlobeControl : RectangleControl
    {
        const int MINIMUM_RENDER_RESOLUTION = 5;
        const int MINIMUM_REQUEST_FACE_SIDE = 8;
        const int MAXIMUM_EXPLICIT_FACE_SIDE = 2048;
        const string SQUARE_SPRITE = "SquareSimple";
        const string CIRCLE_SPRITE = "Circle";
        const float VECTOR_EPSILON = 0.000001f;
        const float VALUE_EPSILON = 0.000001f;

        readonly List<MySprite> _cachedSprites = new List<MySprite>();

        PlanetColorCubemap _cubemap;
        Vector3 _viewDirection = Vector3.Backward;
        Vector3 _screenRight = Vector3.Right;
        Vector3 _screenUp = Vector3.Up;
        Matrix _rotationTransform = Matrix.Identity;
        RectangleF? _clipBounds;
        float _zoom = 1f;
        float _colorAlpha = 1f;
        int _maximumRenderResolution;
        Color _loadingColor = new Color(128, 128, 128, 255);
        Color _selectionBackdropColor = Color.Transparent;
        bool _selectionBackdropVisible;
        float _selectionBackdropExtraDiameterPixels;
        bool _renderCacheDirty = true;
        bool _hasRenderCache;

        public PlanetGlobeControl(RectangleF bounds)
            : base(bounds)
        {
        }

        public PlanetColorCubemap Cubemap => _cubemap;

        public Vector3 ViewDirection => _viewDirection;

        public Vector3 ScreenRightDirection => _screenRight;

        public Vector3 ScreenUpDirection => _screenUp;

        public Matrix RotationTransform => _rotationTransform;

        public float Zoom => _zoom;

        public float ColorAlpha => _colorAlpha;

        public Func<PlanetGlobeControl, Vector2, object, bool> SurfaceClicked { get; set; }

        public override bool CanPrimaryClick
        {
            get { return base.CanPrimaryClick || Visible && Enabled && SurfaceClicked != null; }
        }

        /// <summary>
        /// Maximum square sampling-grid side used to render the globe. Zero keeps
        /// the previous unrestricted behavior.
        /// </summary>
        public int MaximumRenderResolution => _maximumRenderResolution;

        public RectangleF? ClipBounds => _clipBounds;

        public Color LoadingColor => _loadingColor;

        public bool SelectionBackdropVisible => _selectionBackdropVisible;

        public Color SelectionBackdropColor => _selectionBackdropColor;

        public float SelectionBackdropExtraDiameterPixels => _selectionBackdropExtraDiameterPixels;

        public void SetCubemap(PlanetColorCubemap cubemap)
        {
            if (ReferenceEquals(_cubemap, cubemap))
                return;

            _cubemap = cubemap;
            InvalidateRenderCache();
        }

        /// <summary>
        /// Sets the visible hemisphere and its screen-space orientation. All three
        /// directions are planet-local. The tangent axes are normalized and made
        /// orthogonal to the view direction by the control.
        /// </summary>
        public void SetProjection(
            Vector3 viewDirection,
            Vector3 screenRightDirection,
            Vector3 screenUpDirection)
        {
            Vector3 view;
            Vector3 right;
            Vector3 up;
            NormalizeProjection(
                viewDirection,
                screenRightDirection,
                screenUpDirection,
                out view,
                out right,
                out up);

            if (VectorNearlyEquals(_viewDirection, view) &&
                VectorNearlyEquals(_screenRight, right) &&
                VectorNearlyEquals(_screenUp, up))
            {
                return;
            }

            _viewDirection = view;
            _screenRight = right;
            _screenUp = up;
            InvalidateRenderCache();
        }

        /// <summary>
        /// Uses the planet-local +Y axis as screen-up whenever possible. This is
        /// the traditional north-up presentation used by the standalone map app.
        /// </summary>
        public void SetNorthUpProjection(Vector3 viewDirection)
        {
            Vector3 view = NormalizeOrFallback(viewDirection, Vector3.Backward);
            Vector3 referenceUp = Math.Abs(Vector3.Dot(view, Vector3.Up)) > 0.98f
                ? Vector3.Forward
                : Vector3.Up;

            Vector3 right = Vector3.Cross(referenceUp, view);
            if (right.Normalize() <= VECTOR_EPSILON)
                right = Vector3.Right;

            Vector3 up = Vector3.Cross(view, right);
            if (up.Normalize() <= VECTOR_EPSILON)
                up = Vector3.Up;

            SetProjection(view, right, up);
        }

        public void SetRotationTransform(Matrix rotationTransform)
        {
            if (RotationNearlyEquals(_rotationTransform, rotationTransform))
                return;

            _rotationTransform = rotationTransform;
            InvalidateRenderCache();
        }

        public void SetZoom(float zoom)
        {
            float next = Math.Max(0.0001f, zoom);
            if (Math.Abs(_zoom - next) <= VALUE_EPSILON)
                return;

            _zoom = next;
            InvalidateRenderCache();
        }

        public void SetColorAlpha(float alpha)
        {
            float next = MathHelper.Clamp(alpha, 0f, 1f);
            if (Math.Abs(_colorAlpha - next) <= VALUE_EPSILON)
                return;

            _colorAlpha = next;
            InvalidateRenderCache();
        }

        public void SetMaximumRenderResolution(int maximumResolution)
        {
            int next = Math.Max(0, maximumResolution);
            if (_maximumRenderResolution == next)
                return;

            _maximumRenderResolution = next;
            InvalidateRenderCache();
        }

        public void SetLoadingColor(Color color)
        {
            if (_loadingColor.Equals(color))
                return;

            _loadingColor = color;
            InvalidateRenderCache();
        }

        public void SetSelectionBackdrop(bool visible, Color color, float extraDiameterPixels)
        {
            float nextExtraDiameter = Math.Max(0f, extraDiameterPixels);
            if (_selectionBackdropVisible == visible &&
                _selectionBackdropColor.Equals(color) &&
                Math.Abs(_selectionBackdropExtraDiameterPixels - nextExtraDiameter) <= VALUE_EPSILON)
            {
                return;
            }

            _selectionBackdropVisible = visible;
            _selectionBackdropColor = color;
            _selectionBackdropExtraDiameterPixels = nextExtraDiameter;
            MarkDirty();
        }

        /// <summary>
        /// Returns the lazily requested cubemap face side for the current control
        /// bounds and zoom. SquareSimple can represent one independent native Color
        /// per LCD pixel, so the desired face density follows the projected sphere
        /// diameter until MaximumRenderResolution applies a local cap. Power-of-two request buckets allow one completed
        /// cubemap and its mip chain to service nearby zoom levels.
        /// </summary>
        public int GetPreferredFaceSide()
        {
            RectangleF content = GetViewBox();
            float displaySide = Math.Min(content.Width, content.Height);
            float sphereDiameter = displaySide * _zoom;
            if (sphereDiameter <= 0f)
                return MINIMUM_REQUEST_FACE_SIDE;

            int desiredSide = Math.Max(
                MINIMUM_REQUEST_FACE_SIDE,
                (int)Math.Ceiling(sphereDiameter));
            if (_maximumRenderResolution > 0)
                desiredSide = Math.Min(desiredSide, _maximumRenderResolution);

            int requestSide = MINIMUM_REQUEST_FACE_SIDE;
            while (requestSide < desiredSide &&
                   requestSide < MAXIMUM_EXPLICIT_FACE_SIDE)
            {
                requestSide *= 2;
            }

            return requestSide >= desiredSide
                ? requestSide
                : 0;
        }

        public void SetClipBounds(RectangleF? clipBounds)
        {
            if (NullableRectangleNearlyEquals(_clipBounds, clipBounds))
                return;

            _clipBounds = clipBounds;
            InvalidateRenderCache();
        }

        public override void SetRect(RectangleF bounds)
        {
            if (!RectangleNearlyEquals(Rect, bounds))
                _renderCacheDirty = true;

            base.SetRect(bounds);
        }

        protected override bool HitCore(Vector2 point)
        {
            if (_colorAlpha <= 0f)
                return false;

            RectangleF content = GetViewBox();
            float diameter = Math.Min(content.Width, content.Height) * _zoom;
            if (diameter <= 0f)
                return false;

            if (_clipBounds.HasValue && !_clipBounds.Value.Contains(point))
                return false;

            float radius = diameter * 0.5f;
            return Vector2.DistanceSquared(point, content.Center) <= radius * radius;
        }

        public override bool ClickAt(Vector2 point, object sender)
        {
            var clicked = SurfaceClicked;
            if (clicked != null && Hit(point))
                return clicked(this, point, sender);

            return base.ClickAt(point, sender);
        }

        protected override void RenderDefault(List<MySprite> sprites)
        {
            AddSelectionBackdrop(sprites);

            if (_renderCacheDirty || !_hasRenderCache)
                RebuildRenderCache();

            if (_hasRenderCache)
            {
                sprites.AddRange(_cachedSprites);
                return;
            }

            AddLoadingSphere(sprites);
        }

        void AddSelectionBackdrop(List<MySprite> sprites)
        {
            if (!_selectionBackdropVisible || _selectionBackdropColor.A == 0)
                return;

            RectangleF content = GetViewBox();
            if (content.Width <= 0f || content.Height <= 0f)
                return;

            float displaySide = Math.Min(content.Width, content.Height);
            float sphereDiameter = displaySide * _zoom;
            if (sphereDiameter <= 0f)
                return;

            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXTURE,
                Data = CIRCLE_SPRITE,
                Position = content.Center,
                Size = new Vector2(sphereDiameter + _selectionBackdropExtraDiameterPixels * LayoutScale),
                Color = _selectionBackdropColor,
                Alignment = TextAlignment.CENTER
            });
        }

        /// <summary>
        /// Prewarms the sampled planet texture cache. Render calls also rebuild
        /// the cache on demand when projection, bounds, cubemap, or quality changed.
        /// </summary>
        public void UpdateRenderCache()
        {
            if (_renderCacheDirty)
                RebuildRenderCache();
        }

        void RebuildRenderCache()
        {
            _cachedSprites.Clear();
            _renderCacheDirty = false;
            _hasRenderCache = true;

            if (_colorAlpha <= 0f)
                return;

            RectangleF content = GetViewBox();
            if (content.Width <= 0f || content.Height <= 0f)
                return;

            float displaySide = Math.Min(content.Width, content.Height);
            float sphereDiameter = displaySide * _zoom;
            if (sphereDiameter <= 0f)
                return;

            RectangleF sphereBounds = new RectangleF(
                content.Center - new Vector2(sphereDiameter * 0.5f),
                new Vector2(sphereDiameter));
            RectangleF clip = Intersect(
                content,
                _clipBounds.HasValue ? _clipBounds.Value : content);
            if (clip.Width <= 0f || clip.Height <= 0f)
                return;

            if (_cubemap == null)
            {
                AddLoadingSphere(_cachedSprites, sphereBounds);
                return;
            }

            // One cubemap sample per projected LCD pixel. When a sharper
            // cubemap is still loading, the current lower-resolution map remains
            // assigned and is intentionally stretched only for that transition.
            float projectedFaceSamples = Math.Max(1f, sphereDiameter);
            int mipLevel = SelectRenderableMip(projectedFaceSamples);
            int resolution = _cubemap.GetMipResolution(mipLevel);
            if (resolution <= 0)
                return;

            for (int y = 0; y < resolution; y++)
            {
                float cellTop = GetCellEdge(sphereBounds.Y, sphereDiameter, y, resolution);
                float cellBottom = GetCellEdge(sphereBounds.Y, sphereDiameter, y + 1, resolution);
                if (cellBottom <= clip.Y || cellTop >= clip.Bottom)
                    continue;

                float sphereY = 1f - ((y + 0.5f) / resolution) * 2f;
                bool hasRun = false;
                int runStart = 0;
                int runEnd = 0;
                Color runColor = default(Color);

                for (int x = 0; x < resolution; x++)
                {
                    float cellLeft = GetCellEdge(sphereBounds.X, sphereDiameter, x, resolution);
                    float cellRight = GetCellEdge(sphereBounds.X, sphereDiameter, x + 1, resolution);
                    float sphereX = ((x + 0.5f) / resolution) * 2f - 1f;
                    float radiusSquared = sphereX * sphereX + sphereY * sphereY;
                    bool visible = radiusSquared <= 1f &&
                                   cellRight > clip.X &&
                                   cellLeft < clip.Right;

                    if (!visible)
                    {
                        FlushRun(
                            clip,
                            sphereBounds,
                            sphereDiameter,
                            resolution,
                            y,
                            ref hasRun,
                            runStart,
                            runEnd,
                            runColor);
                        continue;
                    }

                    float sphereZ = (float)Math.Sqrt(Math.Max(0f, 1f - radiusSquared));
                    Vector3 displayDirection = _viewDirection * sphereZ +
                                               _screenRight * sphereX +
                                               _screenUp * sphereY;
                    if (displayDirection.Normalize() <= VECTOR_EPSILON)
                        displayDirection = _viewDirection;

                    Vector3 sampleDirection = TransformByInverseRotation(
                        displayDirection,
                        _rotationTransform);
                    if (sampleDirection.Normalize() <= VECTOR_EPSILON)
                        sampleDirection = displayDirection;

                    Color color = ApplyColorAlpha(
                        _cubemap.Sample(sampleDirection, mipLevel),
                        _colorAlpha);

                    if (hasRun && color.Equals(runColor))
                    {
                        runEnd = x;
                        continue;
                    }

                    FlushRun(
                        clip,
                        sphereBounds,
                        sphereDiameter,
                        resolution,
                        y,
                        ref hasRun,
                        runStart,
                        runEnd,
                        runColor);

                    hasRun = true;
                    runStart = x;
                    runEnd = x;
                    runColor = color;
                }

                FlushRun(
                    clip,
                    sphereBounds,
                    sphereDiameter,
                    resolution,
                    y,
                    ref hasRun,
                    runStart,
                    runEnd,
                    runColor);
            }
        }

        void AddLoadingSphere(List<MySprite> sprites)
        {
            if (_colorAlpha <= 0f)
                return;

            RectangleF content = GetViewBox();
            if (content.Width <= 0f || content.Height <= 0f)
                return;

            float displaySide = Math.Min(content.Width, content.Height);
            float sphereDiameter = displaySide * _zoom;
            if (sphereDiameter <= 0f)
                return;

            RectangleF sphereBounds = new RectangleF(
                content.Center - new Vector2(sphereDiameter * 0.5f),
                new Vector2(sphereDiameter));
            AddLoadingSphere(sprites, sphereBounds);
        }

        void AddLoadingSphere(List<MySprite> sprites, RectangleF sphereBounds)
        {
            MySprite sprite = MySprite.CreateSprite(
                CIRCLE_SPRITE,
                sphereBounds.Center,
                sphereBounds.Size);
            sprite.Color = ApplyColorAlpha(_loadingColor, _colorAlpha);
            sprite.Alignment = TextAlignment.CENTER;
            sprites.Add(sprite);
        }

        int SelectRenderableMip(float projectedFaceSamples)
        {
            int mipLevel = _cubemap.SelectMipLevel(projectedFaceSamples);

            if (_maximumRenderResolution > 0)
            {
                while (mipLevel < _cubemap.MipCount - 1 &&
                       _cubemap.GetMipResolution(mipLevel) > _maximumRenderResolution)
                {
                    mipLevel++;
                }
            }

            while (mipLevel > 0 &&
                   _cubemap.GetMipResolution(mipLevel) < MINIMUM_RENDER_RESOLUTION)
            {
                mipLevel--;
            }

            return mipLevel;
        }

        void FlushRun(
            RectangleF clip,
            RectangleF sphereBounds,
            float sphereDiameter,
            int resolution,
            int row,
            ref bool hasRun,
            int firstColumn,
            int lastColumn,
            Color color)
        {
            if (!hasRun)
                return;

            AddSquareRun(
                clip,
                sphereBounds,
                sphereDiameter,
                resolution,
                row,
                firstColumn,
                lastColumn,
                color);
            hasRun = false;
        }

        void AddSquareRun(
            RectangleF clip,
            RectangleF sphereBounds,
            float sphereDiameter,
            int resolution,
            int row,
            int firstColumn,
            int lastColumn,
            Color color)
        {
            float left = GetCellEdge(
                sphereBounds.X,
                sphereDiameter,
                firstColumn,
                resolution);
            float right = GetCellEdge(
                sphereBounds.X,
                sphereDiameter,
                lastColumn + 1,
                resolution);
            float top = GetCellEdge(
                sphereBounds.Y,
                sphereDiameter,
                row,
                resolution);
            float bottom = GetCellEdge(
                sphereBounds.Y,
                sphereDiameter,
                row + 1,
                resolution);

            left = Math.Max(left, clip.X);
            right = Math.Min(right, clip.Right);
            top = Math.Max(top, clip.Y);
            bottom = Math.Min(bottom, clip.Bottom);
            if (right <= left || bottom <= top)
                return;

            RectangleF rectangle = new RectangleF(
                left,
                top,
                right - left,
                bottom - top);
            MySprite sprite = MySprite.CreateSprite(
                SQUARE_SPRITE,
                rectangle.Center,
                rectangle.Size);
            sprite.Color = color;
            sprite.Alignment = TextAlignment.CENTER;
            _cachedSprites.Add(sprite);
        }

        void InvalidateRenderCache()
        {
            _renderCacheDirty = true;
            MarkDirty();
        }

        static void NormalizeProjection(
            Vector3 viewDirection,
            Vector3 screenRightDirection,
            Vector3 screenUpDirection,
            out Vector3 view,
            out Vector3 right,
            out Vector3 up)
        {
            view = NormalizeOrFallback(viewDirection, Vector3.Backward);

            right = screenRightDirection -
                    view * Vector3.Dot(screenRightDirection, view);
            if (right.Normalize() <= VECTOR_EPSILON)
            {
                Vector3 referenceUp = Math.Abs(Vector3.Dot(view, Vector3.Up)) > 0.98f
                    ? Vector3.Forward
                    : Vector3.Up;
                right = Vector3.Cross(referenceUp, view);
                if (right.Normalize() <= VECTOR_EPSILON)
                    right = Vector3.Right;
            }

            up = screenUpDirection -
                 view * Vector3.Dot(screenUpDirection, view) -
                 right * Vector3.Dot(screenUpDirection, right);
            if (up.Normalize() <= VECTOR_EPSILON)
            {
                up = Vector3.Cross(view, right);
                if (up.Normalize() <= VECTOR_EPSILON)
                    up = Vector3.Up;
            }

            if (Vector3.Dot(up, screenUpDirection) < 0f)
                up = -up;
        }

        static Vector3 NormalizeOrFallback(Vector3 value, Vector3 fallback)
        {
            if (value.Normalize() <= VECTOR_EPSILON)
                return fallback;
            return value;
        }

        static Vector3 TransformByInverseRotation(Vector3 direction, Matrix rotation)
        {
            return new Vector3(
                direction.X * rotation.M11 +
                direction.Y * rotation.M12 +
                direction.Z * rotation.M13,
                direction.X * rotation.M21 +
                direction.Y * rotation.M22 +
                direction.Z * rotation.M23,
                direction.X * rotation.M31 +
                direction.Y * rotation.M32 +
                direction.Z * rotation.M33);
        }

        static Color ApplyColorAlpha(Color color, float alpha)
        {
            if (alpha >= 0.999999f)
                return color;

            return new Color(
                color.R,
                color.G,
                color.B,
                (byte)MathHelper.Clamp(
                    (int)Math.Round(color.A * alpha),
                    0,
                    255));
        }

        static float GetCellEdge(
            float origin,
            float span,
            int edgeIndex,
            int resolution)
        {
            return origin + span * edgeIndex / resolution;
        }

        static RectangleF Intersect(RectangleF left, RectangleF right)
        {
            float x = Math.Max(left.X, right.X);
            float y = Math.Max(left.Y, right.Y);
            float r = Math.Min(left.Right, right.Right);
            float b = Math.Min(left.Bottom, right.Bottom);
            return r > x && b > y
                ? new RectangleF(x, y, r - x, b - y)
                : default(RectangleF);
        }

        static bool VectorNearlyEquals(Vector3 left, Vector3 right)
        {
            return Vector3.DistanceSquared(left, right) <= 0.00000001f;
        }

        static bool RectangleNearlyEquals(RectangleF left, RectangleF right)
        {
            return Math.Abs(left.X - right.X) <= 0.0001f &&
                   Math.Abs(left.Y - right.Y) <= 0.0001f &&
                   Math.Abs(left.Width - right.Width) <= 0.0001f &&
                   Math.Abs(left.Height - right.Height) <= 0.0001f;
        }

        static bool NullableRectangleNearlyEquals(RectangleF? left, RectangleF? right)
        {
            if (left.HasValue != right.HasValue)
                return false;
            if (!left.HasValue)
                return true;
            return RectangleNearlyEquals(left.Value, right.Value);
        }

        static bool RotationNearlyEquals(Matrix left, Matrix right)
        {
            return Math.Abs(left.M11 - right.M11) <= VALUE_EPSILON &&
                   Math.Abs(left.M12 - right.M12) <= VALUE_EPSILON &&
                   Math.Abs(left.M13 - right.M13) <= VALUE_EPSILON &&
                   Math.Abs(left.M21 - right.M21) <= VALUE_EPSILON &&
                   Math.Abs(left.M22 - right.M22) <= VALUE_EPSILON &&
                   Math.Abs(left.M23 - right.M23) <= VALUE_EPSILON &&
                   Math.Abs(left.M31 - right.M31) <= VALUE_EPSILON &&
                   Math.Abs(left.M32 - right.M32) <= VALUE_EPSILON &&
                   Math.Abs(left.M33 - right.M33) <= VALUE_EPSILON;
        }
    }
}
