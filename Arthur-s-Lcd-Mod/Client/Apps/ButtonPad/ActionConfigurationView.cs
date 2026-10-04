using Adk.Gui.Controls;

namespace LcdMod.Client.Apps.ButtonPad
{
    internal sealed partial class ActionConfigurationView : Window
    {
        public ActionConfigurationView(ActionConfigurationViewModel model)
        {
            DataContext = model;
            InitializeComponent();
        }
    }
}
