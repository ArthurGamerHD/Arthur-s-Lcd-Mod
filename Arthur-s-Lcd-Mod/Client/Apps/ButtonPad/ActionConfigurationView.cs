using Acrux.Controls;

namespace LcdMod.Client.Apps.ButtonPad
{
    internal sealed partial class ActionConfigurationView : Window
    {
        // Any view model with the bound members works (the IDE preview's clone, for one); the app passes its own.
        public ActionConfigurationView()
        {
            InitializeComponent();
        }

        public ActionConfigurationView(ActionConfigurationViewModel model) : this()
        {
            DataContext = model;
        }
    }
}
