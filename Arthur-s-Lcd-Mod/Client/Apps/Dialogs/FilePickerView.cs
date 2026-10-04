using Adk.Gui.Controls;

namespace LcdMod.Client.Apps.Dialogs
{
    internal sealed partial class FilePickerView : Window
    {
        public FilePickerView(FilePickerViewModel model)
        {
            DataContext = model;
            InitializeComponent();
        }
    }
}
