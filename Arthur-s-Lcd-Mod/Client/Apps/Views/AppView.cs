using Acrux.Controls;

namespace LcdMod.Client.Apps.Views
{
    public sealed partial class AppView : Window
    {
        public AppView()
        {
            AppViewResources.AddTo((key, value) => Resources.Add(key, value));
            InitializeComponent();
        }
    }
}
