using LcdMod.Client.Gui;
using LcdMod.Client.Gui.ControlsTemplates;
using LcdMod.Client.Gui.ControlsTemplates.Panels;
using VRageMath;

namespace LcdMod.Client.Gui.Styling
{
    public static class DefaultStyleBuilder
    {
        public static StyleTree Build()
        {
            StyleTree styles = new StyleTree();

            Style control = styles.ForClass(Control.DefaultStyleClass)
                .Set(ControlTemplate.TextColorProperty, ThemeResources.OnSurfaceColor)
                .Set(ControlTemplate.BackgroundColorProperty, ThemeResources.SurfaceColor)
                .Set(ControlTemplate.BorderColorProperty, ThemeResources.BorderVariantColor)
                .Set(ControlTemplate.BorderRadiusPixelsProperty, Border.DEFAULT_RADIUS_PIXELS)
                .Set(ControlTemplate.BorderThicknessPixelsProperty, 0f)
                .Set(ControlTemplate.PaddingProperty, Vector4.Zero);

            control.State(StyleState.Hover)
                .Set(ControlTemplate.BackgroundColorProperty, ThemeResources.SurfaceContainerColor);

            control.State(StyleState.Disabled)
                .Set(ControlTemplate.BackgroundColorProperty, ThemeResources.SurfaceContainerLowColor)
                .Set(ControlTemplate.TextColorProperty, ThemeResources.OnSurfaceVariantColor);

            control.SetId("Primary")
                .Set(ControlTemplate.BackgroundColorProperty, ThemeResources.AccentContainerColor)
                .Set(ControlTemplate.TextColorProperty, ThemeResources.OnAccentContainerColor);

            control.SetId("Danger")
                .Set(ControlTemplate.BackgroundColorProperty, ThemeResources.ErrorColor)
                .Set(ControlTemplate.TextColorProperty, ThemeResources.OnAccentColor);

            return styles;
        }
    }
}
