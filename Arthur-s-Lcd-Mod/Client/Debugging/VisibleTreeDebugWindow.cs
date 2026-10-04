using Adk.Gui.Controls;
using Adk.Gui.Core.Input;
using Adk.Gui.ScreenSpace;

namespace LcdMod.Client.Debugging
{
    internal sealed partial class VisibleTreeDebugWindow : Window
    {
        public VisibleTreeDebugWindow()
        {
            InitializeComponent();
            Platform = ScreenSpaceWindowPlatform.Instance;
            BindOpen(Key.F11);
        }
    }
}
