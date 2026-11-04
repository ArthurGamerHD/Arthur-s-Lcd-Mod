using Acrux.Controls;

namespace LcdMod.Client.Apps.Dialogs
{
    internal sealed partial class MessageBoxView : Window
    {
        // Any view model with the bound members works (the IDE preview's clone, for one); the app passes its own.
        public MessageBoxView()
        {
            InitializeComponent();
        }

        public MessageBoxView(MessageBoxViewModel model) : this()
        {
            DataContext = model;
        }
    }
}
