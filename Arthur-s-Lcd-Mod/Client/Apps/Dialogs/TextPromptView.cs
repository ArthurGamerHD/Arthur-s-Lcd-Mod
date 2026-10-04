using Adk.Gui.Controls;

namespace LcdMod.Client.Apps.Dialogs
{
    internal sealed partial class TextPromptView : Window
    {
        public TextPromptView(TextPromptViewModel model)
        {
            DataContext = model;
            InitializeComponent();
        }
    }
}
