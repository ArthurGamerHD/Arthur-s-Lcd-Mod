using Adk.Gui.Controls;
using VRageMath;

namespace LcdMod.Client.Apps.Antenna
{
    public sealed partial class AntennaView : UserControl
    {
        public AntennaView()
        {
            InitializeComponent();
        }

        protected override Vector2 MeasureOverride(Vector2 availableSize)
        {
            (DataContext as AntennaViewModel)?.SetViewportWidth(availableSize.X);
            return base.MeasureOverride(availableSize);
        }
    }
}
