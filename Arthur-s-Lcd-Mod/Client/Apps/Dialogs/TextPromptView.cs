using Acrux.Controls;

namespace LcdMod.Client.Apps.Dialogs
{
    internal sealed partial class TextPromptView : Window
    {
        // Any view model with the bound members works (the IDE preview's clone, for one); the app passes its own.
        public TextPromptView()
        {
            InitializeComponent();
        }

        public TextPromptView(TextPromptViewModel model) : this()
        {
            DataContext = model;
        }
    }
}
