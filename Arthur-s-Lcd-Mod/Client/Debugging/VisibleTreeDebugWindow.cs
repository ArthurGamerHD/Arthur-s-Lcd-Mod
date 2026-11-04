using Acrux.Controls;
using Acrux.Core.Input;
using Acrux.ScreenSpace;

namespace LcdMod.Client.Debugging
{
    internal sealed partial class VisibleTreeDebugWindow : Window
    {
        public VisibleTreeDebugWindow()
        {
            InitializeComponent();
            Platform = ScreenSpaceWindowPlatform.Instance;
            BindOpen(Key.OemPeriod);
        }
    }
}
