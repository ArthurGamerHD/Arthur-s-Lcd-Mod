using Acrux.Controls;
using Acrux.Core;
using Acrux.Layout;
using VRageMath;

namespace LcdMod.Client.Apps.EnergyDashboard
{
    internal sealed partial class EnergyDashboardView : UserControl
    {
        const string PANEL_COLOR_RESOURCE = "energyPanelColor";

        public EnergyDashboardView()
        {
            Resources.Add("FontSizeMultiplier", new Views.FontSizeMultiplierConverter());
            InitializeComponent();
        }

        // The panel colour comes from the app's model; without it (a preview clone) the theme's own colour shows.
        public EnergyDashboardView(EnergyDashboardViewModel model) : this()
        {
            Resources.Add(PANEL_COLOR_RESOURCE, model.PanelBackground);
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
            var viewModel = DataContext as EnergyDashboardViewModel;
            if (viewModel != null)
            {
                viewModel.SetViewport(availableSize);
                Color current;
                if (!Resources.TryGetValue(PANEL_COLOR_RESOURCE, out current) || current != viewModel.PanelBackground)
                    Resources.Add(PANEL_COLOR_RESOURCE, viewModel.PanelBackground);
            }
            return base.MeasureOverride(availableSize);
        }
    }
}
