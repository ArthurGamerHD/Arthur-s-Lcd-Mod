using Generated;
using Graph.System.TerminalControls.Scale;
using System.Collections.Generic;
using VRageMath;

namespace Graph.Apps.Utility
{
    public sealed class ClickableText
    {
        public ClickableText(string text, object dataContext = null)
        {
            Text = text;
            DataContext = dataContext;
        }

        public string Text { get; private set; }

        public object DataContext { get; private set; }

        public override string ToString()
        {
            return Text ?? string.Empty;
        }
    }

    public abstract class InteractiveEntry
    {
        protected InteractiveEntry(CursorType cursor, object dataContext = null)
        {
            Cursor = cursor;
            DataContext = dataContext;
        }

        public CursorType Cursor { get; private set; }

        public object DataContext { get; private set; }

        public abstract RectangleF Bounds { get; }

        public abstract bool Hit(Vector2 point);
    }

    public sealed class InteractiveCircleEntry : InteractiveEntry
    {
        public InteractiveCircleEntry(Vector2 center, float radius, CursorType cursor = CursorType.Hand, object dataContext = null)
            : base(cursor, dataContext)
        {
            Center = center;
            Radius = radius;
        }

        public Vector2 Center { get; private set; }
        public float Radius { get; private set; }

        public override RectangleF Bounds
        {
            get
            {
                var size = Radius * 2f;
                return new RectangleF(Center.X - Radius, Center.Y - Radius, size, size);
            }
        }

        public override bool Hit(Vector2 point)
        {
            if (Radius <= 0f)
                return false;

            return Vector2.DistanceSquared(point, Center) <= Radius * Radius;
        }
    }

    public sealed class InteractiveRectangleEntry : InteractiveEntry
    {
        public InteractiveRectangleEntry(RectangleF bounds, CursorType cursor = CursorType.Hand, object dataContext = null)
            : base(cursor, dataContext)
        {
            Rect = bounds;
        }

        public RectangleF Rect { get; private set; }

        public override RectangleF Bounds
        {
            get { return Rect; }
        }

        public override bool Hit(Vector2 point)
        {
            return Rect.Contains(point);
        }
    }

    /// <summary>
    /// Receives gaze coordinates that should be consumed and mapped on the next render frame.
    /// </summary>
    public interface IEyeTracking : IUsesTerminalControl<SliderCursorScale>
    {
        Sandbox.ModAPI.Ingame.IMyTextSurface Surface { get; }
        VRage.Game.ModAPI.Ingame.IMyCubeBlock Block { get; }
        
        int RotationOrSurfaceIndex { get; }

        Vector2 CursorPosition { get; }

        List<InteractiveEntry> InteractiveEntries { get; }
        
        CursorType CursorType { get; }
        
        void LookAt(Vector2 onScreenCoordinates);
    }
}
