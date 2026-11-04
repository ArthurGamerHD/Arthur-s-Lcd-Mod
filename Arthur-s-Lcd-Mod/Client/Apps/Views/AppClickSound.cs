using Acrux.Controls;
using Acrux.Extensions;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.Helpers;

namespace LcdMod.Client.Apps.Views
{
    /// <summary>Plays the HUD click on the app's block whenever one of its buttons is clicked.</summary>
    public sealed class AppClickSound : IClickEffect
    {
        readonly App _app;

        public AppClickSound(App app)
        {
            _app = app;
        }

        public void Play(Button button)
        {
            _app.PlaySound(AudioHelper.HudClick);
        }
    }
}
