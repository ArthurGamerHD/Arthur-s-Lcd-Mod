using LcdMod.Client.Gui.ControlsTemplates;
using LcdMod.Client.Gui.ControlsTemplates.Basic;
using LcdMod.Client.Gui.ControlsTemplates.Panels;
using VRageMath;

namespace LcdMod.Client.Gui.Styling.Styles
{
    public static class DefaultStyleBuilder
    {
        public static StyleTree Build()
        {
            StyleTree styles = new StyleTree();

            Style control = styles.For<ControlTemplate>()
                .Set(ControlTemplate.TextColorProperty, ThemeResources.OnSurfaceColor)
                .Set(ControlTemplate.TextFontProperty, ThemeResources.TextFont)
                .Set(ControlTemplate.LayoutScaleProperty, ThemeResources.LayoutScale)
                .Set(ControlTemplate.FontScaleProperty, ThemeResources.FontScale)
                .Set(ControlTemplate.BackgroundColorProperty, ThemeResources.SurfaceColor)
                .Set(ControlTemplate.BorderColorProperty, ThemeResources.BorderVariantColor)
                .Set(ControlTemplate.BorderRadiusPixelsProperty, Border.DEFAULT_RADIUS_PIXELS)
                .Set(ControlTemplate.BorderThicknessPixelsProperty, 0f)
                .Set(ControlTemplate.PaddingProperty, Vector4.Zero);

            styles.For<ScrollPanel>()
                .Set(ScrollPanel.ScrollBarTrackColorProperty, ThemeResources.ScrollBarTrackColor)
                .Set(ScrollPanel.ScrollBarThumbColorProperty, ThemeResources.ScrollBarThumbColor)
                .Set(ScrollPanel.ScrollBarThumbHoverColorProperty, ThemeResources.ScrollBarThumbHoverColor)
                .Set(ScrollPanel.ScrollBarThumbPressedColorProperty, ThemeResources.ScrollBarThumbPressedColor);

            control.State(StyleState.Hover)
                .Set(ControlTemplate.BackgroundColorProperty, ThemeResources.SurfaceContainerColor);

            control.State(StyleState.Disabled)
                .Set(ControlTemplate.BackgroundColorProperty, ThemeResources.SurfaceContainerLowColor)
                .Set(ControlTemplate.TextColorProperty, ThemeResources.DisabledColor);

            control.SetId("Primary")
                .Set(ControlTemplate.BackgroundColorProperty, ThemeResources.AccentContainerColor)
                .Set(ControlTemplate.TextColorProperty, ThemeResources.OnAccentContainerColor);

            control.SetId("Danger")
                .Set(ControlTemplate.BackgroundColorProperty, ThemeResources.ErrorColor)
                .Set(ControlTemplate.TextColorProperty, ThemeResources.OnAccentColor);

            Style menu = control.ClassSelector("Menu")
                .Set(ControlTemplate.BackgroundColorProperty, ThemeResources.AccentColor)
                .Set(ControlTemplate.TextColorProperty, ThemeResources.OnAccentColor)
                .Set(ControlTemplate.BorderThicknessPixelsProperty, 0f)
                .Set(ControlTemplate.BorderRadiusPixelsProperty, 0f)
                .Set(ControlTemplate.PaddingProperty, Vector4.Zero);

            Style menuItem = control.ClassSelector("MenuItemControl")
                .Set(ControlTemplate.BackgroundColorProperty, Color.Transparent)
                .Set(ControlTemplate.TextColorProperty, ThemeResources.OnSurfaceColor)
                .Set(ControlTemplate.BorderThicknessPixelsProperty, 0f)
                .Set(ControlTemplate.BorderRadiusPixelsProperty, 0f)
                .Set(ControlTemplate.PaddingProperty, Vector4.Zero);

            menuItem.ClassSelector("MenuRootItem")
                .Set(ControlTemplate.TextColorProperty, ThemeResources.OnAccentColor);

            menuItem.ClassSelector("MenuPopupItem")
                .Set(ControlTemplate.TextColorProperty, ThemeResources.OnSurfaceColor);

            menuItem.State(StyleState.Hover)
                .Set(ControlTemplate.BackgroundColorProperty, ThemeResources.AccentContainerColor)
                .Set(ControlTemplate.TextColorProperty, ThemeResources.OnAccentContainerColor);

            menuItem.State(StyleState.Active)
                .Set(ControlTemplate.BackgroundColorProperty, ThemeResources.AccentContainerColor)
                .Set(ControlTemplate.TextColorProperty, ThemeResources.OnAccentContainerColor);

            ButtonStyle.Build(styles);
            
            return styles;
        }
    }
}
