using Adk.Gui.Controls;

namespace LcdMod.Client.Apps.Dialogs
{
    internal sealed partial class MessageBoxView : Window
    {
        public MessageBoxView(MessageBoxViewModel model)
        {
            DataContext = model;
            InitializeComponent();
        }
    }
}
