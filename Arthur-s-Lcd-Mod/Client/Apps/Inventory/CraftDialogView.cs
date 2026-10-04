using Adk.Gui.Controls;

namespace LcdMod.Client.Apps.Inventory
{
    internal sealed partial class CraftDialogView : Window
    {
        public CraftDialogView(CraftDialogViewModel model)
        {
            DataContext = model;
            InitializeComponent();
        }
    }
}
