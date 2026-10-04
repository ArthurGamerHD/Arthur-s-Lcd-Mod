using Adk.Gui.Controls;

namespace LcdMod.Client.Apps.ButtonPad
{
    internal sealed partial class ActionPickerView : Window
    {
        public ActionPickerView(ActionPickerViewModel model)
        {
            DataContext = model;
            InitializeComponent();
        }
    }
}
