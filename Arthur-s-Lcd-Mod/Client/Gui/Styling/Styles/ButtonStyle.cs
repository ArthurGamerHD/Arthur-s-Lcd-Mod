using LcdMod.Client.Gui.ControlsTemplates;
using LcdMod.Client.Gui.ControlsTemplates.Basic;

namespace LcdMod.Client.Gui.Styling.Styles
{
    public static class ButtonStyle
    {
        public static void Build(StyleTree styles)
        {
            Style button = styles.For<Button>();
            
            button.State(StyleState.Hover)
                .Set(ControlTemplate.BackgroundColorProperty, ThemeResources.SurfaceContainerHighColor)
                .Set(ControlTemplate.TextColorProperty, ThemeResources.OnSurfaceColor);

            Style sort = button.ClassSelector("Sort")
                .Set(ControlTemplate.BackgroundColorProperty, ThemeResources.SurfaceColor)
                .Set(ControlTemplate.TextColorProperty, ThemeResources.OnSurfaceVariantColor)
                .Set(ControlTemplate.BorderThicknessPixelsProperty, 0f);

            sort.State(StyleState.Hover)
                .Set(ControlTemplate.BackgroundColorProperty, ThemeResources.SurfaceContainerColor)
                .Set(ControlTemplate.TextColorProperty, ThemeResources.OnSurfaceColor);

            sort.ClassSelector("SortAscending")
                .Set(ControlTemplate.TextColorProperty, ThemeResources.AccentColor);

            sort.ClassSelector("SortDescending")
                .Set(ControlTemplate.TextColorProperty, ThemeResources.AccentColor);
        }
    }
}