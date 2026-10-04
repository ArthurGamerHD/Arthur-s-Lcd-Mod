using Adk.Gui.Controls.Primitives;
using Adk.Gui.Core.Invalidation;
using Adk.Gui.Core.PropertySystem;

namespace LcdMod.Client.Apps.EnergyDashboard
{
    internal sealed class EnergyTierButton : ToggleButton
    {
        public static readonly StyledProperty<string> LabelProperty =
            StyledProperty.Register<EnergyTierButton, string>(nameof(Label), string.Empty, UiInvalidation.Measure);
        public string Label { get { return GetValue(LabelProperty); } set { SetValue(LabelProperty, value); } }

        public static readonly StyledProperty<float> FontSizeProperty =
            StyledProperty.Register<EnergyTierButton, float>(nameof(FontSize), 1f, UiInvalidation.Measure);
        public float FontSize { get { return GetValue(FontSizeProperty); } set { SetValue(FontSizeProperty, value); } }

        public static readonly StyledProperty<string> FontFamilyProperty =
            StyledProperty.Register<EnergyTierButton, string>(nameof(FontFamily), "White", UiInvalidation.Measure);
        public string FontFamily { get { return GetValue(FontFamilyProperty); } set { SetValue(FontFamilyProperty, value); } }
    }
}
