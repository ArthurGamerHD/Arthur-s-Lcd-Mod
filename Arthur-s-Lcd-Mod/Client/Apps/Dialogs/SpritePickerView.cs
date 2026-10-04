using Adk.Gui.Controls;

namespace LcdMod.Client.Apps.Dialogs
{
    internal sealed partial class SpritePickerView : Window
    {
        public SpritePickerView(SpritePickerViewModel model)
        {
            DataContext = model;
            InitializeComponent();
        }
    }
}
