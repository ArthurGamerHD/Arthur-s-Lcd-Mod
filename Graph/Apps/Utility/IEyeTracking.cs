using Generated;
using Graph.System.TerminalControls.Scale;
using System;
using System.Collections.Generic;
using Sandbox.Game;
using Sandbox.ModAPI;
using VRageMath;

namespace Graph.Apps.Utility
{
    public sealed class ClickableText
    {
        public Action<object, object> OnClick { get; private set; }

        public ClickableText(string text, object dataContext = null, Action<object, object> onClick = null)
        {
            Text = text;
            DataContext = dataContext;
            OnClick = onClick;
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
        protected InteractiveEntry(CursorType? cursor = null, object dataContext = null, Action<object, object> onClick = null)
        {
            DataContext = dataContext;
            OnClick = onClick;
            Cursor = cursor ?? (onClick != null ? CursorType.Hand : CursorType.Default);
        }

        public CursorType Cursor { get; private set; }

        public object DataContext { get; private set; }

        public Action<object, object> OnClick { get; private set; }

        public abstract RectangleF Bounds { get; }

        public abstract bool Hit(Vector2 point);

        public void Click(object sender)
        {
            if (OnClick != null)
            {
                MyVisualScriptLogicProvider.PlayHudSoundLocal(playerId: MyAPIGateway.Session.LocalHumanPlayer.Identity.IdentityId);
            }
            OnClick?.Invoke(DataContext ?? this, sender);
        }
    }

    public sealed class InteractiveCircleEntry : InteractiveEntry
    {
        public InteractiveCircleEntry(Vector2 center, float radius, CursorType? cursor = null, object dataContext = null, Action<object, object> onClick = null)
            : base(cursor, dataContext, onClick)
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
        public InteractiveRectangleEntry(RectangleF bounds, CursorType? cursor = null, object dataContext = null, Action<object, object> onClick = null)
            : base(cursor, dataContext, onClick)
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
