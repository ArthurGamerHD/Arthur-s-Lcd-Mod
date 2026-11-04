using Acrux.Controls;

namespace LcdMod.Client.Apps.ButtonPad
{
    internal sealed partial class ActionPickerView : Window
    {
        // Any view model with the bound members works (the IDE preview's clone, for one); the app passes its own.
        public ActionPickerView()
        {
            InitializeComponent();
        }

        public ActionPickerView(ActionPickerViewModel model) : this()
        {
            DataContext = model;
        }
    }
}
