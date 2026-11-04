using Acrux.Controls;

namespace LcdMod.Client.Apps.ButtonPad
{
    internal sealed partial class ButtonPadEntryView : Window
    {
        // Any view model with the bound members works (the IDE preview's clone, for one); the app passes its own.
        public ButtonPadEntryView()
        {
            InitializeComponent();
        }

        public ButtonPadEntryView(ButtonPadEntryViewModel model) : this()
        {
            DataContext = model;
        }
    }
}
