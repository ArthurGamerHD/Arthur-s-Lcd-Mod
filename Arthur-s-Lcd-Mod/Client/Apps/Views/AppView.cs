using Adk.Gui.Controls;

namespace LcdMod.Client.Apps.Views
{
    public sealed partial class AppView : Window
    {
        public AppView()
        {
            var multiplier = new FontSizeMultiplierConverter();
            Resources.Add("FontSizeMultiplier", multiplier);
            Resources.Add("Multiply", new MultiplyConverter());
            Resources.Add("PercentText", new PercentTextConverter());
            Resources.Add("MinimumContrast", new MinimumContrastConverter());
            Resources.Add("donutThickness", 17.6f);
            Resources.Add("donutTextMargin", new VRageMath.Vector4(20f));
            InitializeComponent();
        }
    }
}
