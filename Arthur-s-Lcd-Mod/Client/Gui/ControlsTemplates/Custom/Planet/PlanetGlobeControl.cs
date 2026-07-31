using System;
using System.Collections.Generic;
using LcdMod.Client.Modules.Cartography;
using LcdMod.Common.Helpers;
using Sandbox.ModAPI;
using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace LcdMod.Client.Gui.ControlsTemplates.Custom.Planet
{
    /// <summary>
    /// Renders a mipmapped planet cubemap as an orthographic sphere using native
    /// per-channel text sprites. View and screen-axis directions are expressed in the
    /// same planet-local coordinate frame used by PlanetColorCubemap.
    /// </summary>
    public sealed class PlanetGlobeControl : RectangleControl
    {
        const int MINIMUM_RENDER_RESOLUTION = 5;
        const int MINIMUM_REQUEST_FACE_SIDE = 8;
        const int MAXIMUM_EXPLICIT_FACE_SIDE = 2048;
        const string CIRCLE_SPRITE = "Circle";
        const string ALPHA_MASK_FONT = Constants.MOD_PREFIX + "AlphaMask";
        const char TRANSPARENT_CHANNEL_GLYPH = (char)0xe0ff;
        const int CHANNEL_INTENSITY_GLYPH_BASE = 0xe100;
        const float CHANNEL_TEXT_CELL_PIXELS_1 = 1f;
        const float CHANNEL_TEXT_CELL_PIXELS_2 = 2f;
        const float CHANNEL_TEXT_CELL_PIXELS_4 = 4f;
        const float CHANNEL_TEXT_CELL_PIXELS_8 = 8f;
        const float CHANNEL_TEXT_CELL_PIXELS_16 = 16f;
        const float CHANNEL_TEXT_CELL_PIXELS_32 = 32f;
        const float CHANNEL_TEXT_CELL_PIXELS_64 = 64f;
        const float CHANNEL_TEXT_SPRITE_SCALE_1 = 185f / 576f;
        const float CHANNEL_TEXT_SPRITE_SCALE_2 = 185f / 288f;
        const float CHANNEL_TEXT_SPRITE_SCALE_4 = 185f / 144f;
        const float CHANNEL_TEXT_SPRITE_SCALE_8 = 185f / 72f;
        const float CHANNEL_TEXT_SPRITE_SCALE_16 = 185f / 36f;
        const float CHANNEL_TEXT_SPRITE_SCALE_32 = 185f / 18f;
        const float CHANNEL_TEXT_SPRITE_SCALE_64 = 185f / 9f;
        const int CHANNEL_TEXT_VISIBLE_GUARD_CELLS = 2;
        const int CHANNEL_TEXT_FILL_WORKERS = 4;
        const int CHANNEL_TEXT_PARALLEL_MIN_CELLS = 4096;
        const float VECTOR_EPSILON = 0.000001f;
        const float VALUE_EPSILON = 0.000001f;

        static readonly Color RedChannelTint = new Color(255, 0, 0, 254);
        static readonly Color GreenChannelTint = new Color(0, 255, 0, 128);
        static readonly Color BlueChannelTint = new Color(0, 0, 255, 85);

        readonly List<MySprite> _cachedSprites = new List<MySprite>();
        readonly MutableChannelText _redChannelText = new MutableChannelText();
        readonly MutableChannelText _greenChannelText = new MutableChannelText();
        readonly MutableChannelText _blueChannelText = new MutableChannelText();

        PlanetColorCubemap _cubemap;
        Vector3 _viewDirection = Vector3.Backward;
        Vector3 _screenRight = Vector3.Right;
        Vector3 _screenUp = Vector3.Up;
        Matrix _rotationTransform = Matrix.Identity;
        RectangleF? _clipBounds;
        float _zoom = 1f;
        float _colorAlpha = 1f;
        int _maximumRenderResolution;
        float _channelTextCellPixels = CHANNEL_TEXT_CELL_PIXELS_1;
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

        public Func<PlanetGlobeControl, Vector2, object, bool> SurfaceMiddleClicked { get; set; }

        public override bool CanPrimaryClick
        {
            get { return base.CanPrimaryClick || Visible && Enabled && SurfaceClicked != null; }
        }

        public override bool CanMiddleClick
        {
            get { return base.CanMiddleClick || Visible && Enabled && SurfaceMiddleClicked != null; }
        }

        /// <summary>
        /// Maximum square sampling-grid side used to render the globe. Zero keeps
        /// the previous unrestricted behavior.
        /// </summary>
        public int MaximumRenderResolution => _maximumRenderResolution;

        public float ChannelTextCellPixels => _channelTextCellPixels;

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
            SetRenderQuality(maximumResolution, _channelTextCellPixels);
        }

        public void SetRenderQuality(int maximumResolution, float channelTextCellPixels)
        {
            int nextMaximumResolution = Math.Max(0, maximumResolution);
            float nextCellPixels = NormalizeChannelTextCellPixels(channelTextCellPixels);
            if (_maximumRenderResolution == nextMaximumResolution &&
                Math.Abs(_channelTextCellPixels - nextCellPixels) <= VALUE_EPSILON)
            {
                return;
            }

            _maximumRenderResolution = nextMaximumResolution;
            _channelTextCellPixels = nextCellPixels;
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
        /// bounds and zoom. The desired face density follows the fixed-scale
        /// channel text grid until MaximumRenderResolution applies a local cap.
        /// Power-of-two request buckets allow one completed cubemap and its mip
        /// chain to service nearby zoom levels.
        /// </summary>
        public int GetPreferredFaceSide()
        {
            RectangleF content = GetViewBox();
            float displaySide = Math.Min(content.Width, content.Height);
            float sphereDiameter = displaySide * _zoom;
            if (sphereDiameter <= 0f)
                return MINIMUM_REQUEST_FACE_SIDE;

            int maximumRows = _maximumRenderResolution > 0 ? _maximumRenderResolution : int.MaxValue;
            ChannelTextRenderPreset renderPreset = SelectChannelTextRenderPreset(sphereDiameter, maximumRows);
            int desiredSide = Math.Max(
                MINIMUM_REQUEST_FACE_SIDE,
                GetChannelTextSide(sphereDiameter, renderPreset.CellPixels, maximumRows));

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

        public bool TryGetSurfaceDirection(Vector2 point, out Vector3 localDirection)
        {
            localDirection = Vector3.Zero;
            if (!Hit(point))
                return false;

            RectangleF content = GetViewBox();
            float diameter = Math.Min(content.Width, content.Height) * _zoom;
            if (diameter <= 0f)
                return false;

            float radius = diameter * 0.5f;
            if (radius <= 0f)
                return false;

            float x = (point.X - content.Center.X) / radius;
            float y = (content.Center.Y - point.Y) / radius;
            float radiusSquared = x * x + y * y;
            if (radiusSquared > 1.000001f)
                return false;

            float z = (float)Math.Sqrt(Math.Max(0f, 1f - radiusSquared));
            Vector3 displayDirection =
                _viewDirection * z +
                _screenRight * x +
                _screenUp * y;
            if (displayDirection.Normalize() <= VECTOR_EPSILON)
                return false;

            localDirection = TransformByInverseRotation(
                displayDirection,
                _rotationTransform);
            return localDirection.Normalize() > VECTOR_EPSILON;
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

        public override bool MiddleClickAt(Vector2 point, object sender)
        {
            var clicked = SurfaceMiddleClicked;
            if (clicked != null && Hit(point))
                return clicked(this, point, sender);

            return base.MiddleClickAt(point, sender);
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

            // Prefer fixed cell-size presets before selecting the mip. If the
            // 64 px preset still cannot cover the capped render area, calculate
            // the exact cell size for this frame instead of cropping the globe.
            int maximumRows = _maximumRenderResolution > 0 ? _maximumRenderResolution : int.MaxValue;
            ChannelTextRenderPreset renderPreset = SelectChannelTextRenderPreset(sphereDiameter, maximumRows);
            int desiredRows = GetChannelTextSide(sphereDiameter, renderPreset.CellPixels, maximumRows);
            int mipLevel = SelectRenderableMip(desiredRows);
            int resolution = _cubemap.GetMipResolution(mipLevel);
            if (resolution <= 0)
                return;

            if (desiredRows > resolution)
            {
                renderPreset = SelectChannelTextRenderPreset(
                    sphereDiameter,
                    Math.Min(maximumRows, resolution));
                desiredRows = GetChannelTextSide(
                    sphereDiameter,
                    renderPreset.CellPixels,
                    Math.Min(maximumRows, resolution));
                mipLevel = SelectRenderableMip(desiredRows);
                resolution = _cubemap.GetMipResolution(mipLevel);
                if (resolution <= 0)
                    return;
            }

            int fullRows = desiredRows;
            int fullColumns = GetChannelTextColumns(fullRows);
            if (fullColumns <= 0)
                return;

            ChannelTextWindow window;
            if (!TryGetVisibleChannelTextWindow(
                    clip,
                    sphereBounds,
                    fullColumns,
                    fullRows,
                    out window))
            {
                return;
            }

            EnsureChannelTextSize(window.Columns, window.Rows);
            FillChannelText(window, mipLevel);
            AddChannelTextSprites(clip, sphereBounds, window, renderPreset);
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

        int GetChannelTextSide(
            float sphereDiameter,
            float cellPixels,
            int maximumRows)
        {
            if (sphereDiameter <= 0f)
                return MINIMUM_RENDER_RESOLUTION;

            int side = (int)Math.Ceiling(sphereDiameter / Math.Max(1f, cellPixels));
            side = Math.Max(MINIMUM_RENDER_RESOLUTION, side);
            if (maximumRows > 0 && maximumRows != int.MaxValue)
                side = Math.Min(side, maximumRows);
            return side;
        }

        static int GetChannelTextColumns(int rows)
        {
            return Math.Max(0, rows);
        }

        ChannelTextRenderPreset SelectChannelTextRenderPreset(
            float sphereDiameter,
            int maximumRows)
        {
            float cellPixels = _channelTextCellPixels;
            while (maximumRows > 0 &&
                   maximumRows != int.MaxValue &&
                   cellPixels < CHANNEL_TEXT_CELL_PIXELS_64 &&
                   (int)Math.Ceiling(sphereDiameter / cellPixels) > maximumRows)
            {
                cellPixels = GetNextChannelTextCellPixels(cellPixels);
            }

            if (maximumRows > 0 &&
                maximumRows != int.MaxValue &&
                (int)Math.Ceiling(sphereDiameter / cellPixels) > maximumRows)
            {
                return GetRuntimeChannelTextRenderPreset(sphereDiameter, maximumRows);
            }

            return GetChannelTextRenderPreset(cellPixels);
        }

        static float NormalizeChannelTextCellPixels(float cellPixels)
        {
            if (cellPixels <= CHANNEL_TEXT_CELL_PIXELS_1 + VALUE_EPSILON)
                return CHANNEL_TEXT_CELL_PIXELS_1;
            if (cellPixels <= CHANNEL_TEXT_CELL_PIXELS_2 + VALUE_EPSILON)
                return CHANNEL_TEXT_CELL_PIXELS_2;
            if (cellPixels <= CHANNEL_TEXT_CELL_PIXELS_4 + VALUE_EPSILON)
                return CHANNEL_TEXT_CELL_PIXELS_4;
            return CHANNEL_TEXT_CELL_PIXELS_8;
        }

        static float GetNextChannelTextCellPixels(float cellPixels)
        {
            if (cellPixels < CHANNEL_TEXT_CELL_PIXELS_2)
                return CHANNEL_TEXT_CELL_PIXELS_2;
            if (cellPixels < CHANNEL_TEXT_CELL_PIXELS_4)
                return CHANNEL_TEXT_CELL_PIXELS_4;
            if (cellPixels < CHANNEL_TEXT_CELL_PIXELS_8)
                return CHANNEL_TEXT_CELL_PIXELS_8;
            if (cellPixels < CHANNEL_TEXT_CELL_PIXELS_16)
                return CHANNEL_TEXT_CELL_PIXELS_16;
            if (cellPixels < CHANNEL_TEXT_CELL_PIXELS_32)
                return CHANNEL_TEXT_CELL_PIXELS_32;
            if (cellPixels < CHANNEL_TEXT_CELL_PIXELS_64)
                return CHANNEL_TEXT_CELL_PIXELS_64;
            return CHANNEL_TEXT_CELL_PIXELS_64;
        }

        static ChannelTextRenderPreset GetChannelTextRenderPreset(float cellPixels)
        {
            if (cellPixels <= CHANNEL_TEXT_CELL_PIXELS_1 + VALUE_EPSILON)
                return new ChannelTextRenderPreset(CHANNEL_TEXT_CELL_PIXELS_1, CHANNEL_TEXT_SPRITE_SCALE_1);
            if (cellPixels <= CHANNEL_TEXT_CELL_PIXELS_2 + VALUE_EPSILON)
                return new ChannelTextRenderPreset(CHANNEL_TEXT_CELL_PIXELS_2, CHANNEL_TEXT_SPRITE_SCALE_2);
            if (cellPixels <= CHANNEL_TEXT_CELL_PIXELS_4 + VALUE_EPSILON)
                return new ChannelTextRenderPreset(CHANNEL_TEXT_CELL_PIXELS_4, CHANNEL_TEXT_SPRITE_SCALE_4);
            if (cellPixels <= CHANNEL_TEXT_CELL_PIXELS_8 + VALUE_EPSILON)
                return new ChannelTextRenderPreset(CHANNEL_TEXT_CELL_PIXELS_8, CHANNEL_TEXT_SPRITE_SCALE_8);
            if (cellPixels <= CHANNEL_TEXT_CELL_PIXELS_16 + VALUE_EPSILON)
                return new ChannelTextRenderPreset(CHANNEL_TEXT_CELL_PIXELS_16, CHANNEL_TEXT_SPRITE_SCALE_16);
            if (cellPixels <= CHANNEL_TEXT_CELL_PIXELS_32 + VALUE_EPSILON)
                return new ChannelTextRenderPreset(CHANNEL_TEXT_CELL_PIXELS_32, CHANNEL_TEXT_SPRITE_SCALE_32);
            return new ChannelTextRenderPreset(CHANNEL_TEXT_CELL_PIXELS_64, CHANNEL_TEXT_SPRITE_SCALE_64);
        }

        static ChannelTextRenderPreset GetRuntimeChannelTextRenderPreset(
            float sphereDiameter,
            int maximumRows)
        {
            double cellPixels = Math.Max(
                CHANNEL_TEXT_CELL_PIXELS_64,
                (double)sphereDiameter / Math.Max(1, maximumRows));

            cellPixels *= 1.000001d;
            return new ChannelTextRenderPreset(
                (float)cellPixels,
                (float)(cellPixels * 185d / 576d));
        }

        static bool TryGetVisibleChannelTextWindow(
            RectangleF clip,
            RectangleF sphereBounds,
            int fullColumns,
            int fullRows,
            out ChannelTextWindow window)
        {
            window = default(ChannelTextWindow);

            if (fullColumns <= 0 || fullRows <= 0)
                return false;

            float sphereDiameter = Math.Min(sphereBounds.Width, sphereBounds.Height);
            if (sphereDiameter <= 0f)
                return false;

            RectangleF visible = Intersect(clip, sphereBounds);
            if (visible.Width <= 0f || visible.Height <= 0f)
                return false;

            int columnStart = ClampInt(
                (int)Math.Floor((visible.X - sphereBounds.X) * fullColumns / sphereDiameter),
                0,
                fullColumns);
            int columnEnd = ClampInt(
                (int)Math.Ceiling((visible.Right - sphereBounds.X) * fullColumns / sphereDiameter),
                0,
                fullColumns);
            int rowStart = ClampInt(
                (int)Math.Floor((visible.Y - sphereBounds.Y) * fullRows / sphereDiameter),
                0,
                fullRows);
            int rowEnd = ClampInt(
                (int)Math.Ceiling((visible.Bottom - sphereBounds.Y) * fullRows / sphereDiameter),
                0,
                fullRows);

            columnStart = Math.Max(0, columnStart - CHANNEL_TEXT_VISIBLE_GUARD_CELLS);
            columnEnd = Math.Min(fullColumns, columnEnd + CHANNEL_TEXT_VISIBLE_GUARD_CELLS);
            rowStart = Math.Max(0, rowStart - CHANNEL_TEXT_VISIBLE_GUARD_CELLS);
            rowEnd = Math.Min(fullRows, rowEnd + CHANNEL_TEXT_VISIBLE_GUARD_CELLS);

            if (columnEnd <= columnStart || rowEnd <= rowStart)
                return false;

            window = new ChannelTextWindow(
                fullColumns,
                fullRows,
                columnStart,
                rowStart,
                columnEnd - columnStart,
                rowEnd - rowStart);
            return true;
        }

        void EnsureChannelTextSize(int columns, int rows)
        {
            _redChannelText.EnsureSize(columns, rows);
            _greenChannelText.EnsureSize(columns, rows);
            _blueChannelText.EnsureSize(columns, rows);
        }

        void FillChannelText(ChannelTextWindow window, int mipLevel)
        {
            char[] red = _redChannelText.Buffer;
            char[] green = _greenChannelText.Buffer;
            char[] blue = _blueChannelText.Buffer;
            int cellCount = window.Columns * window.Rows;
            if (red == null || green == null || blue == null || cellCount <= 0)
                return;

            var context = new ChannelTextFillContext(
                window,
                mipLevel,
                red,
                green,
                blue,
                _cubemap,
                _viewDirection,
                _screenRight,
                _screenUp,
                _rotationTransform);

            if (MyAPIGateway.Parallel != null &&
                cellCount >= CHANNEL_TEXT_PARALLEL_MIN_CELLS)
            {
                MyAPIGateway.Parallel.For(
                    0,
                    CHANNEL_TEXT_FILL_WORKERS,
                    workerOffset => FillChannelTextStride(
                        context,
                        workerOffset,
                        CHANNEL_TEXT_FILL_WORKERS));
            }
            else
            {
                FillChannelTextStride(context, 0, 1);
            }

            FillChannelTextNewlines(window, red, green, blue);

            _redChannelText.Commit();
            _greenChannelText.Commit();
            _blueChannelText.Commit();
        }

        static void FillChannelTextStride(
            ChannelTextFillContext context,
            int offset,
            int stride)
        {
            ChannelTextWindow window = context.Window;
            int cellCount = window.Columns * window.Rows;
            int rowStride = window.Columns + 1;

            for (int i = offset; i < cellCount; i += stride)
            {
                int y = i / window.Columns;
                int x = i - y * window.Columns;
                int rowOffset = y * rowStride;
                int logicalY = window.RowStart + y;
                float sphereY = 1f - ((logicalY + 0.5f) / window.FullRows) * 2f;

                int textIndex = rowOffset + x;
                char redChar = TRANSPARENT_CHANNEL_GLYPH;
                char greenChar = TRANSPARENT_CHANNEL_GLYPH;
                char blueChar = TRANSPARENT_CHANNEL_GLYPH;

                int logicalX = window.ColumnStart + x;
                float sphereX = ((logicalX + 0.5f) / window.FullColumns) * 2f - 1f;
                float radiusSquared = sphereX * sphereX + sphereY * sphereY;
                if (radiusSquared <= 1f)
                {
                    float sphereZ = (float)Math.Sqrt(Math.Max(0f, 1f - radiusSquared));
                    Vector3 displayDirection = context.ViewDirection * sphereZ +
                                               context.ScreenRight * sphereX +
                                               context.ScreenUp * sphereY;
                    if (displayDirection.Normalize() <= VECTOR_EPSILON)
                        displayDirection = context.ViewDirection;

                    Vector3 sampleDirection = TransformByInverseRotation(
                        displayDirection,
                        context.RotationTransform);
                    if (sampleDirection.Normalize() <= VECTOR_EPSILON)
                        sampleDirection = displayDirection;

                    Color color = context.Cubemap.Sample(sampleDirection, context.MipLevel);
                    if (color.A != 0)
                    {
                        redChar = IntensityToChar(color.R);
                        greenChar = IntensityToChar(color.G);
                        blueChar = IntensityToChar(color.B);
                    }
                }

                context.Red[textIndex] = redChar;
                context.Green[textIndex] = greenChar;
                context.Blue[textIndex] = blueChar;
            }
        }

        static void FillChannelTextNewlines(
            ChannelTextWindow window,
            char[] red,
            char[] green,
            char[] blue)
        {
            int rowStride = window.Columns + 1;
            for (int y = 0; y + 1 < window.Rows; y++)
            {
                int newlineIndex = y * rowStride + window.Columns;
                red[newlineIndex] = '\n';
                green[newlineIndex] = '\n';
                blue[newlineIndex] = '\n';
            }
        }

        void AddChannelTextSprites(
            RectangleF clip,
            RectangleF sphereBounds,
            ChannelTextWindow window,
            ChannelTextRenderPreset renderPreset)
        {
            double fullTextWidth = Math.Max(1, window.FullColumns) * renderPreset.CellPixels;
            double fullTextHeight = Math.Max(1, window.FullRows) * renderPreset.CellPixels;
            if (fullTextWidth <= 0d || fullTextHeight <= 0d)
                return;

            double visibleOffsetX = window.ColumnStart * renderPreset.CellPixels;
            double visibleOffsetY = window.RowStart * renderPreset.CellPixels;
            Vector2 center = sphereBounds.Center;
            Vector2 position = new Vector2(
                (float)((double)center.X - fullTextWidth * 0.5d + visibleOffsetX),
                (float)((double)center.Y - fullTextHeight * 0.5d + visibleOffsetY));

            if (!BeginContentClip(_cachedSprites, clip))
                return;

            AddChannelTextSprite(_redChannelText.Text, position, renderPreset.SpriteScale, ApplyChannelAlpha(RedChannelTint));
            AddChannelTextSprite(_greenChannelText.Text, position, renderPreset.SpriteScale, ApplyChannelAlpha(GreenChannelTint));
            AddChannelTextSprite(_blueChannelText.Text, position, renderPreset.SpriteScale, ApplyChannelAlpha(BlueChannelTint));
            EndContentClip(_cachedSprites);
        }

        void AddChannelTextSprite(
            string text,
            Vector2 position,
            float spriteScale,
            Color channelTint)
        {
            if (channelTint.A == 0 || string.IsNullOrEmpty(text))
                return;

            _cachedSprites.Add(new MySprite
            {
                Type = SpriteType.TEXT,
                Data = text,
                Position = position,
                RotationOrScale = spriteScale,
                Color = channelTint,
                Alignment = TextAlignment.LEFT,
                FontId = ALPHA_MASK_FONT
            });
        }

        Color ApplyChannelAlpha(Color color)
        {
            if (_colorAlpha >= 0.999999f)
                return color;

            return new Color(
                color.R,
                color.G,
                color.B,
                (byte)MathHelper.Clamp(
                    (int)Math.Round(color.A * _colorAlpha),
                    0,
                    255));
        }

        static char IntensityToChar(byte intensity)
        {
            return (char)(CHANNEL_INTENSITY_GLYPH_BASE + intensity);
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

        static int ClampInt(int value, int min, int max)
        {
            if (value < min)
                return min;
            return value > max
                ? max
                : value;
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

        struct ChannelTextRenderPreset
        {
            public readonly float CellPixels;
            public readonly float SpriteScale;

            public ChannelTextRenderPreset(float cellPixels, float spriteScale)
            {
                CellPixels = cellPixels;
                SpriteScale = spriteScale;
            }
        }

        struct ChannelTextWindow
        {
            public readonly int FullColumns;
            public readonly int FullRows;
            public readonly int ColumnStart;
            public readonly int RowStart;
            public readonly int Columns;
            public readonly int Rows;

            public ChannelTextWindow(
                int fullColumns,
                int fullRows,
                int columnStart,
                int rowStart,
                int columns,
                int rows)
            {
                FullColumns = fullColumns;
                FullRows = fullRows;
                ColumnStart = columnStart;
                RowStart = rowStart;
                Columns = columns;
                Rows = rows;
            }
        }

        struct ChannelTextFillContext
        {
            public readonly ChannelTextWindow Window;
            public readonly int MipLevel;
            public readonly char[] Red;
            public readonly char[] Green;
            public readonly char[] Blue;
            public readonly PlanetColorCubemap Cubemap;
            public readonly Vector3 ViewDirection;
            public readonly Vector3 ScreenRight;
            public readonly Vector3 ScreenUp;
            public readonly Matrix RotationTransform;

            public ChannelTextFillContext(
                ChannelTextWindow window,
                int mipLevel,
                char[] red,
                char[] green,
                char[] blue,
                PlanetColorCubemap cubemap,
                Vector3 viewDirection,
                Vector3 screenRight,
                Vector3 screenUp,
                Matrix rotationTransform)
            {
                Window = window;
                MipLevel = mipLevel;
                Red = red;
                Green = green;
                Blue = blue;
                Cubemap = cubemap;
                ViewDirection = viewDirection;
                ScreenRight = screenRight;
                ScreenUp = screenUp;
                RotationTransform = rotationTransform;
            }
        }

        sealed class MutableChannelText
        {
            int _columns;
            int _rows;

            public string Text { get; private set; }

            public char[] Buffer { get; private set; }

            public void EnsureSize(int columns, int rows)
            {
                if (columns == _columns && rows == _rows && Text != null && Buffer != null)
                    return;

                _columns = Math.Max(0, columns);
                _rows = Math.Max(0, rows);

                if (_columns == 0 || _rows == 0)
                {
                    Text = null;
                    Buffer = null;
                    return;
                }

                int length = _columns * _rows + Math.Max(0, _rows - 1);
                Buffer = new char[length];
                for (int i = 0; i < Buffer.Length; i++)
                    Buffer[i] = TRANSPARENT_CHANNEL_GLYPH;
                Text = null;
            }

            public void Commit()
            {
                if (Buffer == null || Buffer.Length == 0)
                    return;

                if (Text != null && Text.Length == Buffer.Length)
                {
                    bool unchanged = true;
                    for (int i = 0; i < Buffer.Length; i++)
                    {
                        if (Text[i] == Buffer[i])
                            continue;

                        unchanged = false;
                        break;
                    }

                    if (unchanged)
                        return;
                }

                Text = new string(Buffer);
            }
        }
    }
}
