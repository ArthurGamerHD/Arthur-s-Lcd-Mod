using Acrux.Controls;

namespace LcdMod.Client.Apps.CargoFilled
{
    internal sealed partial class ContainerActionView : Window
    {
        // Any view model with the bound members works (the IDE preview's clone, for one); the app passes its own.
        public ContainerActionView()
        {
            InitializeComponent();
        }

        public ContainerActionView(ContainerActionViewModel model) : this()
        {
            DataContext = model;
        }
    }
}
