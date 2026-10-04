using Adk.Gui.Controls;

namespace LcdMod.Client.Apps.CargoFilled
{
    internal sealed partial class ContainerActionView : Window
    {
        public ContainerActionView(ContainerActionViewModel model)
        {
            DataContext = model;
            InitializeComponent();
        }
    }
}
