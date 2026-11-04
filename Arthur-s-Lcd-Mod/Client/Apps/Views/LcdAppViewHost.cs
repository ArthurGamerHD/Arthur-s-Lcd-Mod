using System;
using System.Collections.Generic;
using Acrux.Animation;
using Acrux.Extensions;
using Acrux.Lcd;
using LcdMod.Client.Apps.Abstract;
using VRage.Game.GUI.TextPanel;
using VRageMath;
using IMyTextSurface = Sandbox.ModAPI.Ingame.IMyTextSurface;

namespace LcdMod.Client.Apps.Views
{
    public sealed class LcdAppViewHost : IDisposable
    {
        public LcdAppViewHost(
            IMyTextSurface surface,
            App app,
            AnimationController animations)
        {
            if (app == null)
                throw new ArgumentNullException(nameof(app));
            _app = app;
            View = new AppView { DataContext = app };
            View.Resources.Add(ClickEffect.ResourceKey, new AppClickSound(app));
            ClickEffect.Install();
            _platform = new LcdWindowPlatform(surface, null, animations, false);
            _platform.RenderScalingProvider = () => app.WindowRenderScaling;
            View.Platform = _platform;
            View.Open();
            GuiHost = _platform.GetGuiHost(View);
        }

        readonly LcdWindowPlatform _platform;
        readonly App _app;
        public AppView View { get; private set; }

        public LcdGuiHost GuiHost { get; private set; }

        public void Draw(RectangleF bounds, List<MySprite> sprites)
        {
            if (GuiHost == null)
                return;
            _platform.Draw(View, bounds, sprites);
            _app.CompleteRetainedRender();
        }

        public void Dispose()
        {
            if (View != null)
                View.Dispose();
            View = null;
            GuiHost = null;
        }
    }
}
