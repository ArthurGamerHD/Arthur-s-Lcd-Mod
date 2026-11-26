using Acrux.Controls;
using Acrux.Extensions;
using Acrux.Styling;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.Apps.Views;

namespace LcdMod.Client.Apps.MultiFunction
{
    internal sealed partial class HostedAppView : Window
    {
        public HostedAppView()
        {
            AppViewResources.AddTo((key, value) => Resources.Add(key, value));
            InitializeComponent();
        }

        public HostedAppView(App app) : this()
        {
            DataContext = app;
            Resources.Add(ClickEffect.ResourceKey, new AppClickSound(app));
            ClickEffect.Install();
        }

        float _contentScale = 1f;

        /// <summary>Scales the app inside the window, as its own scale setting does on its own surface.</summary>
        internal void SetContentScale(float scale)
        {
            if (scale <= 0f || float.IsNaN(scale) || float.IsInfinity(scale))
                scale = 1f;
            if (System.Math.Abs(scale - _contentScale) < .0001f)
                return;
            _contentScale = scale;
            ScaleHost.LayoutTransform = new ScaleTransform(scale, scale);
        }
    }
}
