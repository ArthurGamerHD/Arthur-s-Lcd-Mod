using System;
using Acrux.Core;
using Acrux.Core.Invalidation;
using Acrux.Core.PropertySystem;
using Acrux.Rendering;
using VRageMath;

namespace LcdMod.Client.Apps.Maps
{
    /// <summary>A pegman dragged onto the <see cref="CartographyMapView"/> of the same tree to open street view there.</summary>
    internal sealed class StreetViewHandle : VisualElement
    {
        public static readonly StyledProperty<Color> ForegroundProperty =
            StyledProperty.Register<StreetViewHandle, Color>(nameof(Foreground), Color.White, UiInvalidation.Render);
        public static readonly StyledProperty<Color> DisabledForegroundProperty =
            StyledProperty.Register<StreetViewHandle, Color>(nameof(DisabledForeground), Color.Gray, UiInvalidation.Render);
        public static readonly StyledProperty<bool> IsAvailableProperty =
            StyledProperty.Register<StreetViewHandle, bool>(nameof(IsAvailable), true, UiInvalidation.Render);

        const string ICON = "StreetView";
        const float DRAG_THRESHOLD_SQUARED = 16f;

        Vector2 _pressPoint;
        Vector2 _pointer;
        bool _dragging;

        public Color Foreground
        {
            get { return GetValue(ForegroundProperty); }
            set { SetValue(ForegroundProperty, value); }
        }

        public Color DisabledForeground
        {
            get { return GetValue(DisabledForegroundProperty); }
            set { SetValue(DisabledForegroundProperty, value); }
        }

        public bool IsAvailable
        {
            get { return GetValue(IsAvailableProperty); }
            set { SetValue(IsAvailableProperty, value); }
        }

        internal bool IsDragging => _dragging;

        protected override bool HitTestSelf(Vector2 point)
        {
            return true;
        }

        protected override void DrawSelf(RenderContext context)
        {
            float side = Math.Min(Bounds.Width, Bounds.Height) * .75f;
            Color color = IsAvailable ? Foreground : DisabledForeground;
            context.Texture(ICON, new RectangleF(Bounds.Center - new Vector2(side * .5f), new Vector2(side)), color, true);
            if (!_dragging)
                return;

            var ghost = new Color(color.R, color.G, color.B, 209);
            context.Texture("CircleHollow", new RectangleF(_pointer - new Vector2(side / 6f), new Vector2(side / 3f)), Foreground);
            context.Texture(ICON, new RectangleF(_pointer + new Vector2(side / 3f, -side / 3f) - new Vector2(side * .5f),
                new Vector2(side)), ghost, true);
        }

        internal override void PointerPressed(Vector2 point)
        {
            _pressPoint = point;
            _pointer = point;
            _dragging = false;
        }

        internal override void PointerMoved(Vector2 point, Vector2 delta)
        {
            if (!IsPressed || !IsAvailable)
                return;
            _pointer = point;
            if (!_dragging && Vector2.DistanceSquared(_pressPoint, point) >= DRAG_THRESHOLD_SQUARED)
                _dragging = true;
            if (_dragging)
                Invalidate(UiInvalidation.Render);
        }

        internal override void PointerReleased(Vector2 point, bool clicked)
        {
            bool dragged = _dragging;
            _dragging = false;
            Invalidate(UiInvalidation.Render);
            if (!dragged || !IsAvailable)
                return;
            CartographyMapView map = FindMap(this);
            if (map != null)
                map.DropStreetView(point);
        }

        static CartographyMapView FindMap(VisualElement element)
        {
            VisualElement root = element;
            while (root.Parent != null)
                root = root.Parent;
            return Find(root);
        }

        static CartographyMapView Find(VisualElement element)
        {
            var map = element as CartographyMapView;
            if (map != null)
                return map;
            var children = element.VisualChildren;
            for (int i = 0; i < children.Count; i++)
            {
                CartographyMapView found = Find(children[i]);
                if (found != null)
                    return found;
            }
            return null;
        }
    }
}
