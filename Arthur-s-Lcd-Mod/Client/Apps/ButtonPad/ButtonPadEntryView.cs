using Adk.Gui.Controls;

namespace LcdMod.Client.Apps.ButtonPad
{
    internal sealed partial class ButtonPadEntryView : Window
    {
        public ButtonPadEntryView(ButtonPadEntryViewModel model)
        {
            DataContext = model;
            InitializeComponent();
        }
    }
}
