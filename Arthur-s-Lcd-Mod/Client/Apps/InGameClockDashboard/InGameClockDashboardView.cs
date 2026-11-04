using Acrux.Controls;
using VRageMath;

namespace LcdMod.Client.Apps.InGameClockDashboard
{
    internal sealed partial class InGameClockDashboardView : UserControl
    {
        public InGameClockDashboardView()
        {
            InitializeComponent();
        }

        protected override Vector2 MeasureOverride(Vector2 availableSize)
        {
            (DataContext as IClockDashboardViewport)?.SetViewport(availableSize);
            return base.MeasureOverride(availableSize);
        }
    }
}
