using System.Collections.Generic;
using System;
using LcdMod.Client.Apps.Abstract;
using Adk.Mvvm;
using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace LcdMod.Client.Apps.Views
{
    public abstract class SpriteAppViewModel : ObservableObject
    {
        readonly App _app;
        int _renderVersion;

        protected SpriteAppViewModel(App app)
        {
            _app = app;
            Renderer = RenderCanvas;
        }

        public App App => _app;
        public int RenderVersion => _renderVersion;
        public Func<object, RectangleF, IReadOnlyList<MySprite>> Renderer { get; private set; }

        static readonly MySprite[] NoSprites = new MySprite[0];

        public virtual IReadOnlyList<MySprite> Render(RectangleF bounds)
        {
            // Without an app (design-time data in the IDE preview) there is nothing to draw.
            if (_app == null)
                return NoSprites;
            _app.SetRetainedContentBounds(bounds);
            return _app.GetSprites();
        }

        IReadOnlyList<MySprite> RenderCanvas(object dataContext, RectangleF bounds)
        {
            return Render(bounds);
        }

        public void Invalidate()
        {
            _renderVersion++;
            RaisePropertyChanged<int>(nameof(RenderVersion));
        }
    }
}
