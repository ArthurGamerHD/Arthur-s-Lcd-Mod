using System;
using System.Collections.Generic;
using Adk.Gui.Core;
using Adk.Gui.Core.Input;
using Adk.Gui.Core.Invalidation;
using Adk.Gui.Core.PropertySystem;
using Adk.Gui.Rendering;
using Sandbox.ModAPI;
using VRageMath;

namespace LcdMod.Client.Apps.Maps
{
    /// <summary>
    /// Shows a cartography map rendered by its <see cref="IMapSurface"/> into a generated texture sized to the control's
    /// pixels, with the surface markers on top. Pointer input is forwarded to the surface in canvas pixels.
    /// </summary>
    internal sealed class CartographyMapView : VisualElement
    {
        public static readonly StyledProperty<IMapSurface> SurfaceProperty =
            StyledProperty.Register<CartographyMapView, IMapSurface>(nameof(Surface), null, UiInvalidation.Render);
        public static readonly StyledProperty<int> VersionProperty =
            StyledProperty.Register<CartographyMapView, int>(nameof(Version), 0, UiInvalidation.Render);
        public static readonly StyledProperty<IReadOnlyList<MapMarker>> MarkersProperty =
            StyledProperty.Register<CartographyMapView, IReadOnlyList<MapMarker>>(nameof(Markers), null, UiInvalidation.Render);
        public static readonly StyledProperty<int> MarkersVersionProperty =
            StyledProperty.Register<CartographyMapView, int>(nameof(MarkersVersion), 0, UiInvalidation.Render);
        public static readonly StyledProperty<string> FontFamilyProperty =
            StyledProperty.Register<CartographyMapView, string>(nameof(FontFamily), "White", UiInvalidation.Render);
        public static readonly StyledProperty<float> MarkerScaleProperty =
            StyledProperty.Register<CartographyMapView, float>(nameof(MarkerScale), 1f, UiInvalidation.Render);

        const int DOUBLE_CLICK_FRAMES = 21;
        const float DRAG_THRESHOLD_SQUARED = 16f;
        const float DOUBLE_CLICK_DISTANCE_SQUARED = 64f;
        const float MARKER_SIZE = 14f;
        const float MARKER_LABEL_GAP = 5f;
        const float MARKER_LABEL_SCALE = .5f;
        const float PLAYER_MARKER_SIZE = 30f;
        static readonly Color Shadow = new Color(0, 0, 0, 210);

        IDynamicTexture _texture;
        IDynamicTextureFactory _factory;
        IMapSurface _textureSurface;
        int _paintedVersion = -1;
        Vector2 _pressPoint;
        bool _dragging;
        Vector2 _middlePressPoint;
        bool _middleDragging;
        Vector2 _lastClickPoint;
        int _lastClickFrame;

        public IMapSurface Surface
        {
            get { return GetValue(SurfaceProperty); }
            set { SetValue(SurfaceProperty, value); }
        }

        public int Version
        {
            get { return GetValue(VersionProperty); }
            set { SetValue(VersionProperty, value); }
        }

        public IReadOnlyList<MapMarker> Markers
        {
            get { return GetValue(MarkersProperty); }
            set { SetValue(MarkersProperty, value); }
        }

        public int MarkersVersion
        {
            get { return GetValue(MarkersVersionProperty); }
            set { SetValue(MarkersVersionProperty, value); }
        }

        public string FontFamily
        {
            get { return GetValue(FontFamilyProperty); }
            set { SetValue(FontFamilyProperty, value); }
        }

        public float MarkerScale
        {
            get { return GetValue(MarkerScaleProperty); }
            set { SetValue(MarkerScaleProperty, value); }
        }

        internal Vector2I CanvasSize => _texture != null && _texture.IsAvailable ? _texture.Size : Vector2I.Zero;

        internal bool TryMapToCanvas(Vector2 point, out Vector2 pixel)
        {
            pixel = default(Vector2);
            Vector2I size = CanvasSize;
            if (size.X <= 0 || size.Y <= 0 || Bounds.Width <= 0f || Bounds.Height <= 0f || !Bounds.Contains(point))
                return false;
            pixel = (point - Bounds.Position) * new Vector2(size.X / Bounds.Width, size.Y / Bounds.Height);
            return true;
        }

        internal void DropStreetView(Vector2 point)
        {
            Vector2 pixel;
            IMapSurface surface = Surface;
            if (surface != null && TryMapToCanvas(point, out pixel))
                surface.StreetView(pixel);
        }

        protected override bool HitTestSelf(Vector2 point)
        {
            return Surface != null;
        }

        protected override void DrawSelf(RenderContext context)
        {
            IMapSurface surface = Surface;
            if (surface == null || Bounds.Width <= 0f || Bounds.Height <= 0f)
                return;
            if (!EnsureTexture(context, surface))
                return;
            context.DrawTexture(_texture.Source, Bounds, Color.White);
            DrawMarkers(context);
        }

        bool EnsureTexture(RenderContext context, IMapSurface surface)
        {
            IDynamicTextureFactory factory = context.DynamicTextures;
            if (factory == null)
                return false;
            float scaling = Math.Max(.0001f, context.RenderScaling);
            var requested = new Vector2I(
                Math.Max(1, (int)Math.Round(Bounds.Width * scaling)),
                Math.Max(1, (int)Math.Round(Bounds.Height * scaling)));
            Vector2I size = factory.FitToSupportedSize(requested);
            if (size.X <= 0 || size.Y <= 0)
                return false;

            bool created = false;
            var pendingTexture = _texture as AsyncMapTexture;
            // Finish the accepted frame before resizing; repeated layout changes
            // must not spawn overlapping jobs or throw away work in progress.
            if (pendingTexture != null && pendingTexture.IsPending && ReferenceEquals(_textureSurface, surface))
            {
                context.MarkVolatile();
                return true;
            }
            if (_texture == null || !_texture.IsAvailable || !ReferenceEquals(_factory, factory) ||
                !ReferenceEquals(_textureSurface, surface) || _texture.Size != size)
            {
                Release();
                _texture = surface is IAsyncMapSurface
                    ? new AsyncMapTexture(size)
                    : factory.Create("CartographyMap", size);
                _factory = factory;
                _textureSurface = surface;
                _paintedVersion = -1;
                created = true;
                if (_texture != null && _texture.IsAvailable)
                    surface.SetCanvasSize(_texture.Size);
            }
            if (_texture == null || !_texture.IsAvailable)
                return false;
            if (_paintedVersion != Version)
            {
                var asyncTexture = _texture as AsyncMapTexture;
                if (asyncTexture != null)
                {
                    int requestedVersion = Version;
                    asyncTexture.Request((IAsyncMapSurface)surface, published =>
                    {
                        if (!ReferenceEquals(_texture, asyncTexture))
                            return;
                        if (published)
                            _paintedVersion = requestedVersion;
                        Invalidate(UiInvalidation.Render);
                    });
                    context.MarkVolatile();
                }
                else if (_texture.Update(surface.Paint))
                    _paintedVersion = Version;
            }
            if (!created)
                return true;
            context.MarkVolatile();
            return false;
        }

        void DrawMarkers(RenderContext context)
        {
            IReadOnlyList<MapMarker> markers = Markers;
            Vector2I size = CanvasSize;
            if (markers == null || size.X <= 0 || size.Y <= 0)
                return;
            float scale = Math.Max(.1f, MarkerScale);
            for (int i = 0; i < markers.Count; i++)
            {
                MapMarker marker = markers[i];
                Vector2 center = ToLocal(marker.Position, size);
                if (marker.Kind == MapMarkerKind.Player)
                {
                    DrawPlayer(context, marker, center, scale);
                    continue;
                }

                float side = MARKER_SIZE * scale * marker.Size;
                if (marker.Shadow)
                    context.Texture(marker.Icon, Square(center + new Vector2(scale), side * 1.25f), Shadow, false,
                        marker.Rotation);
                context.Texture(marker.Icon, Square(center, side), marker.Color, true, marker.Rotation);
                float textScale = MARKER_LABEL_SCALE * scale;
                if (!string.IsNullOrEmpty(marker.CountText))
                {
                    context.Text(FontFamily, marker.CountText, center + new Vector2(scale), textScale * .8f, Shadow,
                        HorizontalAlignment.Center, VerticalAlignment.Center);
                    context.Text(FontFamily, marker.CountText, center, textScale * .8f, marker.Color,
                        HorizontalAlignment.Center, VerticalAlignment.Center);
                }
                if (string.IsNullOrEmpty(marker.Label))
                    continue;
                HorizontalAlignment alignment = marker.CenterLabel ? HorizontalAlignment.Center : HorizontalAlignment.Left;
                var labelPosition = marker.CenterLabel
                    ? center
                    : new Vector2(center.X + side * .5f + MARKER_LABEL_GAP * scale, center.Y);
                if (marker.Shadow)
                    context.Text(FontFamily, marker.Label, labelPosition + new Vector2(scale), textScale, Shadow,
                        alignment, VerticalAlignment.Center);
                context.Text(FontFamily, marker.Label, labelPosition, textScale, marker.Color,
                    alignment, VerticalAlignment.Center);
            }
        }

        static void DrawPlayer(RenderContext context, MapMarker marker, Vector2 center, float scale)
        {
            float side = PLAYER_MARKER_SIZE * scale * marker.Size;
            context.Texture(marker.Icon, Square(center + new Vector2(scale, scale * 2f), side), new Color(0, 0, 0, 190), true,
                marker.Rotation);
            context.Texture(marker.Icon, Square(center, side), marker.Color, true, marker.Rotation);
        }

        static RectangleF Square(Vector2 center, float side)
        {
            return new RectangleF(center - new Vector2(side * .5f), new Vector2(side));
        }

        Vector2 ToLocal(Vector2 pixel, Vector2I size)
        {
            return Bounds.Position + pixel * new Vector2(Bounds.Width / size.X, Bounds.Height / size.Y);
        }

        Vector2 ToCanvasDelta(Vector2 delta)
        {
            Vector2I size = CanvasSize;
            if (size.X <= 0 || size.Y <= 0 || Bounds.Width <= 0f || Bounds.Height <= 0f)
                return delta;
            return delta * new Vector2(size.X / Bounds.Width, size.Y / Bounds.Height);
        }

        MapMarker MarkerAt(Vector2 point)
        {
            IReadOnlyList<MapMarker> markers = Markers;
            Vector2I size = CanvasSize;
            if (markers == null || size.X <= 0 || size.Y <= 0)
                return null;
            float radius = MARKER_SIZE * Math.Max(.1f, MarkerScale);
            for (int i = markers.Count - 1; i >= 0; i--)
            {
                MapMarker marker = markers[i];
                float hit = radius * Math.Max(1f, marker.Size);
                if (marker.Clickable && Vector2.DistanceSquared(point, ToLocal(marker.Position, size)) <= hit * hit)
                    return marker;
            }
            return null;
        }

        internal override void PointerPressed(Vector2 point)
        {
            _pressPoint = point;
            _dragging = false;
            var drag = Surface as IMapSurfaceDrag;
            if (drag != null)
                drag.BeginDrag(ToCanvasDelta(point - Bounds.Position));
        }

        internal override PointerCursor GetPointerCursor(Vector2 point)
        {
            return (_dragging && IsPressed) || _middleDragging
                ? PointerCursor.DragMove
                : base.GetPointerCursor(point);
        }

        internal override void PointerCaptureLost()
        {
            _dragging = false;
            _middleDragging = false;
            var drag = Surface as IMapSurfaceDrag;
            if (drag != null)
                drag.CancelDrag();
        }

        internal override void PointerMoved(Vector2 point, Vector2 delta)
        {
            IMapSurface surface = Surface;
            if (surface == null || !IsPressed)
                return;
            if (!_dragging && Vector2.DistanceSquared(_pressPoint, point) >= DRAG_THRESHOLD_SQUARED)
                _dragging = true;
            if (_dragging && delta != Vector2.Zero)
            {
                var drag = surface as IMapSurfaceDrag;
                if (drag != null)
                    drag.DragTo(ToCanvasDelta(point - Bounds.Position), ToCanvasDelta(delta));
                else
                    surface.Drag(ToCanvasDelta(delta));
            }
        }

        internal override void PointerReleased(Vector2 point, bool clicked)
        {
            IMapSurface surface = Surface;
            bool dragged = _dragging;
            _dragging = false;
            var drag = surface as IMapSurfaceDrag;
            if (drag != null)
                drag.EndDrag();
            if (surface == null || !clicked || dragged)
                return;

            MapMarker marker = MarkerAt(point);
            if (marker != null)
            {
                surface.MarkerClicked(marker);
                return;
            }

            Vector2 pixel;
            if (!TryMapToCanvas(point, out pixel))
                return;
            int frame = CurrentFrame();
            int elapsed = unchecked(frame - _lastClickFrame);
            if (_lastClickFrame != 0 && elapsed >= 0 && elapsed <= DOUBLE_CLICK_FRAMES &&
                Vector2.DistanceSquared(_lastClickPoint, point) <= DOUBLE_CLICK_DISTANCE_SQUARED)
            {
                _lastClickFrame = 0;
                surface.DoubleClick(pixel);
                return;
            }
            _lastClickFrame = frame;
            _lastClickPoint = point;
            surface.Click(pixel);
        }

        internal override void MiddlePointerPressed(Vector2 point)
        {
            _middlePressPoint = point;
            _middleDragging = false;
        }

        internal override void MiddlePointerMoved(Vector2 point, Vector2 delta)
        {
            IMapSurface surface = Surface;
            if (surface == null)
                return;
            if (!_middleDragging && Vector2.DistanceSquared(_middlePressPoint, point) >= DRAG_THRESHOLD_SQUARED)
                _middleDragging = true;
            if (_middleDragging && delta != Vector2.Zero)
                surface.MiddleDrag(ToCanvasDelta(delta));
        }

        internal override void MiddlePointerReleased(Vector2 point)
        {
            IMapSurface surface = Surface;
            bool dragged = _middleDragging;
            _middleDragging = false;
            Vector2 pixel;
            if (surface != null && !dragged && TryMapToCanvas(point, out pixel))
                surface.MiddleClick(pixel);
        }

        internal override bool PointerScrolled(Vector2 point, int delta)
        {
            IMapSurface surface = Surface;
            return surface != null && delta != 0 && surface.Scroll(delta);
        }

        static int CurrentFrame()
        {
            var session = MyAPIGateway.Session;
            return session.GameplayFrameCounter;
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
