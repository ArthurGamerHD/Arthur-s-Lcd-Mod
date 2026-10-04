using System.Collections.Generic;
using System;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Common.Mvvm;
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

        public virtual IReadOnlyList<MySprite> Render(RectangleF bounds)
        {
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
