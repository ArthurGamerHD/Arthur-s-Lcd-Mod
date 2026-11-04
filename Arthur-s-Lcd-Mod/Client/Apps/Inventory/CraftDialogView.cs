using Acrux.Controls;

namespace LcdMod.Client.Apps.Inventory
{
    internal sealed partial class CraftDialogView : Window
    {
        // Any view model with the bound members works (the IDE preview's clone, for one); the app passes its own.
        public CraftDialogView()
        {
            InitializeComponent();
        }

        public CraftDialogView(CraftDialogViewModel model) : this()
        {
            DataContext = model;
        }
    }
}
