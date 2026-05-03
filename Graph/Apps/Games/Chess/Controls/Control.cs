using System;
using System.Collections.Generic;
using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace Graph.Apps.Games.Chess
{
    public abstract class Control : IDisposable
    {
        public bool Disposed { get; protected set; }
        public readonly List<RectangleF> Boxes = new List<RectangleF>();

        public abstract void Render(List<MySprite> frame);

        public abstract void HandleCommand(string command);
        
        public virtual void Dispose()
        {
            if(Disposed)
                return;
            Disposed = true;
            Boxes.Clear();
        }

        public abstract void ClickBox(int index);
    }
}