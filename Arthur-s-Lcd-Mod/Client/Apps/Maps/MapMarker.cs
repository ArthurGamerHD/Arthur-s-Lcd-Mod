using VRageMath;

namespace LcdMod.Client.Apps.Maps
{
    internal enum MapMarkerKind
    {
        Gps,
        Radio,
        Player
    }

    /// <summary>A marker drawn over the map texture. <see cref="Position"/> is in map canvas pixels.</summary>
    internal sealed class MapMarker
    {
        public MapMarkerKind Kind;
        public Vector2 Position;
        public string Icon;
        public Color Color;
        public string Label;
        public string CountText;
        public float Rotation;
        public float Size = 1f;
        public string Name;
        public Vector3D WorldPosition;
        public bool Clickable;
        public bool CenterLabel;
        public float Depth;
        public bool Shadow = true;
    }

    internal interface IMapSurface
    {
        void SetCanvasSize(Vector2I size);
        void Paint(Acrux.Rendering.PixelCanvas canvas);
        void Drag(Vector2 delta);
        void MiddleDrag(Vector2 delta);
        void MiddleClick(Vector2 pixel);
        void Click(Vector2 pixel);
        void DoubleClick(Vector2 pixel);
        bool Scroll(int delta);
        void MarkerClicked(MapMarker marker);
        void StreetView(Vector2 pixel);
    }

    /// <summary>Requests one captured map frame; completion runs on the calling UI thread.</summary>
    internal interface IAsyncMapSurface
    {
        bool RequestTexture(Acrux.Rendering.PixelCanvas canvas, System.Action<System.Exception> completed);
    }

    // The pointer resting over the map, in canvas pixels; null once it has left.
    internal interface IMapSurfaceHover
    {
        void Hover(Vector2? pixel);
    }

    internal interface IMapSurfaceDrag
    {
        void BeginDrag(Vector2 pixel);
        void DragTo(Vector2 pixel, Vector2 delta);
        void EndDrag();
        void CancelDrag();
    }
}
