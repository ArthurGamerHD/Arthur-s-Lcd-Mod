using Adk.Gui.Controls;
using Adk.Gui.Core;
using Adk.Gui.Layout;
using VRageMath;

namespace LcdMod.Client.Apps.EnergyDashboard
{
    internal sealed partial class EnergyDashboardView : UserControl
    {
        public EnergyDashboardView(EnergyDashboardViewModel model)
        {
            Resources.Add("FontSizeMultiplier", new Views.FontSizeMultiplierConverter());
            InitializeComponent();
            DataContext = model;
        }

        public bool IsPointerOverList()
        {
            return IsPointerOverList(this);
        }

        static bool IsPointerOverList(VisualElement element)
        {
            var scroller = element as ScrollViewer;
            if (scroller != null)
                return scroller.Visible && scroller.IsPointerOver;
            var children = element.VisualChildren;
            for (int i = 0; i < children.Count; i++)
                if (children[i].Visible && IsPointerOverList(children[i]))
                    return true;
            return false;
        }

        protected override Vector2 MeasureOverride(Vector2 availableSize)
        {
            (DataContext as EnergyDashboardViewModel)?.SetViewport(availableSize);
            return base.MeasureOverride(availableSize);
        }
    }
}
