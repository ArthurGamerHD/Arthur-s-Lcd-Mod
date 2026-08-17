using LcdMod.Client.Animation;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.Apps.ViewModel;
using LcdMod.Client.Gui.ControlsTemplates;
using LcdMod.Client.Gui.ControlsTemplates.Basic;
using LcdMod.Client.Gui.ControlsTemplates.Lists;

namespace LcdMod.Client.Gui.Styling.Styles
{
    public static class ButtonStyle
    {
        public static void Build(StyleTree styles)
        {
            Style<Button> button = styles.For<Button>();
            ConfigureStandardButton(button);

            Style<ToggleButton> toggleButton = button.For<ToggleButton>()
                .Set(ControlTemplate.BackgroundColorProperty, ThemeResources.AccentContainerColor)
                .Set(ControlTemplate.TextColorProperty, ThemeResources.OnAccentContainerColor);

            toggleButton.State(StyleState.Active)
                .Set(ControlTemplate.BackgroundColorProperty, ThemeResources.AccentColor)
                .Set(ControlTemplate.TextColorProperty, ThemeResources.OnAccentColor);

            Style sort = button.ClassSelector("Sort")
                .Set(ControlTemplate.BackgroundColorProperty, ThemeResources.SurfaceColor)
                .Set(ControlTemplate.TextColorProperty, ThemeResources.OnSurfaceVariantColor)
                .Set(ControlTemplate.BorderThicknessPixelsProperty, 0f);

            sort.State(StyleState.Hover)
                .Set(ControlTemplate.BackgroundColorProperty, ThemeResources.SurfaceContainerColor)
                .Set(ControlTemplate.TextColorProperty, ThemeResources.OnSurfaceColor)
                .Set(ControlTemplate.BorderThicknessPixelsProperty, 0f);

            sort.State(StyleState.Selected)
                .Set(ControlTemplate.BorderThicknessPixelsProperty, 0f);

            sort.State(StyleState.Pressed)
                .Set(ControlTemplate.BorderThicknessPixelsProperty, 0f);

            sort.ClassSelector("SortAscending")
                .Set(ControlTemplate.TextColorProperty, ThemeResources.AccentColor);

            sort.ClassSelector("SortDescending")
                .Set(ControlTemplate.TextColorProperty, ThemeResources.AccentColor);

            Style pageArrow = button.SetId("PageArrow")
                .Set(ControlTemplate.BorderThicknessPixelsProperty, 0f);
            pageArrow.State(StyleState.Hover)
                .Set(ControlTemplate.BorderThicknessPixelsProperty, 0f);
            pageArrow.State(StyleState.Selected)
                .Set(ControlTemplate.BorderThicknessPixelsProperty, 0f);
            pageArrow.State(StyleState.Pressed)
                .Set(ControlTemplate.BorderThicknessPixelsProperty, 0f);

            Style scrollBarThumb = button.SetId("ScrollBarThumb");
            ConfigureScaleUpOnly(scrollBarThumb);

            Style listBoxItem = styles.For<ListBoxItem<ItemEntry>>();
            ConfigureListBoxItem(listBoxItem);

            Style listRow = styles.For<ControlTemplate>().ClassSelector("Row");
            ConfigureListRow(listRow);
        }

        static void ConfigureListRow(Style style)
        {
            style
                .Set(ControlTemplate.BackgroundColorProperty, ThemeResources.SecondaryContainerColor)
                .Set(ControlTemplate.TextColorProperty, ThemeResources.OnSecondaryContainerColor)
                .Set(ControlTemplate.RenderTransformProperty, ScaleTransform.Identity)
                .Animate(ControlTemplate.RenderTransformProperty, 6, EasingMode.EaseOutCubic,
                    AnimationInterpolators.RenderTransform)
                .Animate(ControlTemplate.BackgroundColorProperty, 6, EasingMode.EaseOutCubic,
                    AnimationInterpolators.Color)
                .Animate(ControlTemplate.TextColorProperty, 6, EasingMode.EaseOutCubic,
                    AnimationInterpolators.Color)
                .Animate(ControlTemplate.BorderColorProperty, 6, EasingMode.EaseOutCubic,
                    AnimationInterpolators.Color);

            style.State(StyleState.Hover)
                .Set(ControlTemplate.BackgroundColorProperty, ThemeResources.SurfaceContainerHighColor)
                .Set(ControlTemplate.TextColorProperty, ThemeResources.OnSurfaceColor)
                .Set(ControlTemplate.RenderTransformProperty, ScaleTransform.Identity);

            style.State(StyleState.Pressed)
                .Set(ControlTemplate.BackgroundColorProperty, ThemeResources.SurfaceContainerHighestColor)
                .Set(ControlTemplate.TextColorProperty, ThemeResources.OnSurfaceColor)
                .Set(ControlTemplate.RenderTransformProperty, new ScaleTransform(0.95f));

            Style selectedState = style.State(StyleState.Selected)
                .Set(ControlTemplate.BackgroundColorProperty, ThemeResources.AccentContainerColor)
                .Set(ControlTemplate.TextColorProperty, ThemeResources.OnAccentContainerColor);

            selectedState.State(StyleState.Hover)
                .Set(ControlTemplate.BackgroundColorProperty, ThemeResources.AccentColor)
                .Set(ControlTemplate.TextColorProperty, ThemeResources.OnAccentColor);

            Style selected = style.ClassSelector("Selected")
                .Set(ControlTemplate.BackgroundColorProperty, ThemeResources.AccentContainerColor)
                .Set(ControlTemplate.TextColorProperty, ThemeResources.OnAccentContainerColor);

            selected.State(StyleState.Hover)
                .Set(ControlTemplate.BackgroundColorProperty, ThemeResources.AccentColor)
                .Set(ControlTemplate.TextColorProperty, ThemeResources.OnAccentColor);
        }

        static void ConfigureListBoxItem(Style style)
        {
            style
                .Set(ControlTemplate.BackgroundColorProperty, ThemeResources.SecondaryContainerColor)
                .Set(ControlTemplate.TextColorProperty, ThemeResources.OnSecondaryContainerColor)
                .Set(ControlTemplate.RenderTransformProperty, ScaleTransform.Identity)
                .Animate(
                    ControlTemplate.RenderTransformProperty,
                    6,
                    EasingMode.EaseOutCubic,
                    AnimationInterpolators.RenderTransform)
                .Animate(
                    ControlTemplate.BackgroundColorProperty,
                    6,
                    EasingMode.EaseOutCubic,
                    AnimationInterpolators.Color)
                .Animate(
                    ControlTemplate.TextColorProperty,
                    6,
                    EasingMode.EaseOutCubic,
                    AnimationInterpolators.Color);

            style.State(StyleState.Hover)
                .Set(ControlTemplate.BackgroundColorProperty, ThemeResources.SurfaceContainerHighColor)
                .Set(ControlTemplate.TextColorProperty, ThemeResources.OnSurfaceColor)
                .Set(ControlTemplate.RenderTransformProperty, ScaleTransform.Identity)
                .Animate(
                    ControlTemplate.RenderTransformProperty,
                    5,
                    EasingMode.EaseOutCubic,
                    AnimationInterpolators.RenderTransform)
                .Animate(
                    ControlTemplate.BackgroundColorProperty,
                    5,
                    EasingMode.EaseOutCubic,
                    AnimationInterpolators.Color)
                .Animate(
                    ControlTemplate.TextColorProperty,
                    5,
                    EasingMode.EaseOutCubic,
                    AnimationInterpolators.Color);

            style.State(StyleState.Selected)
                .Set(ControlTemplate.BackgroundColorProperty, ThemeResources.AccentContainerColor)
                .Set(ControlTemplate.TextColorProperty, ThemeResources.OnAccentContainerColor);

            // More-specific combined states are declared after their individual
            // states so they win when both flags are active.
            style.State(StyleState.Hover | StyleState.Selected)
                .Set(ControlTemplate.BackgroundColorProperty, ThemeResources.AccentColor)
                .Set(ControlTemplate.TextColorProperty, ThemeResources.OnAccentColor)
                .Set(ControlTemplate.RenderTransformProperty, ScaleTransform.Identity);

            // Pressed remains the strongest transient interaction state.
            style.State(StyleState.Pressed)
                .Set(ControlTemplate.BackgroundColorProperty, ThemeResources.SurfaceContainerHighestColor)
                .Set(ControlTemplate.TextColorProperty, ThemeResources.OnSurfaceColor)
                .Set(ControlTemplate.RenderTransformProperty, new ScaleTransform(0.95f))
                .Animate(
                    ControlTemplate.RenderTransformProperty,
                    2,
                    EasingMode.EaseOutCubic,
                    AnimationInterpolators.RenderTransform)
                .Animate(
                    ControlTemplate.BackgroundColorProperty,
                    2,
                    EasingMode.EaseOutCubic,
                    AnimationInterpolators.Color)
                .Animate(
                    ControlTemplate.TextColorProperty,
                    2,
                    EasingMode.EaseOutCubic,
                    AnimationInterpolators.Color);

            style.State(StyleState.Dragged)
                .Set(ControlTemplate.BackgroundColorProperty, ThemeResources.SurfaceContainerHighestColor)
                .Set(ControlTemplate.TextColorProperty, ThemeResources.OnSurfaceColor)
                .Set(ControlTemplate.RenderTransformProperty, ScaleTransform.Identity)
                .Animate(
                    ControlTemplate.RenderTransformProperty,
                    2,
                    EasingMode.EaseOutCubic,
                    AnimationInterpolators.RenderTransform)
                .Animate(
                    ControlTemplate.BackgroundColorProperty,
                    2,
                    EasingMode.EaseOutCubic,
                    AnimationInterpolators.Color)
                .Animate(
                    ControlTemplate.TextColorProperty,
                    2,
                    EasingMode.EaseOutCubic,
                    AnimationInterpolators.Color);
        }

        static void ConfigureStandardButton(Style style)
        {
            style
                .Set(ControlTemplate.RenderTransformProperty, ScaleTransform.Identity)
                .Set(ControlTemplate.BorderColorProperty, ThemeResources.BorderVariantColor)
                .Set(ControlTemplate.BorderThicknessPixelsProperty, 0f)
                .Animate(
                    ControlTemplate.RenderTransformProperty,
                    6,
                    EasingMode.EaseOutCubic,
                    AnimationInterpolators.RenderTransform)
                .Animate(
                    ControlTemplate.BackgroundColorProperty,
                    6,
                    EasingMode.EaseOutCubic,
                    AnimationInterpolators.Color)
                .Animate(
                    ControlTemplate.TextColorProperty,
                    6,
                    EasingMode.EaseOutCubic,
                    AnimationInterpolators.Color)
                .Animate(
                    ControlTemplate.BorderColorProperty,
                    6,
                    EasingMode.EaseOutCubic,
                    AnimationInterpolators.Color)
                .Animate(
                    ControlTemplate.BorderThicknessPixelsProperty,
                    6,
                    EasingMode.EaseOutCubic,
                    AnimationInterpolators.Float);

            style.State(StyleState.Hover)
                .Set(ControlTemplate.BackgroundColorProperty, ThemeResources.SurfaceContainerHighColor)
                .Set(ControlTemplate.TextColorProperty, ThemeResources.OnSurfaceColor)
                .Set(ControlTemplate.BorderColorProperty, ThemeResources.BorderColor)
                .Set(ControlTemplate.BorderThicknessPixelsProperty, 2f)
                .Set(ControlTemplate.RenderTransformProperty, new ScaleTransform(1.05f))
                .Animate(
                    ControlTemplate.RenderTransformProperty,
                    5,
                    EasingMode.EaseOutCubic,
                    AnimationInterpolators.RenderTransform)
                .Animate(
                    ControlTemplate.BackgroundColorProperty,
                    5,
                    EasingMode.EaseOutCubic,
                    AnimationInterpolators.Color)
                .Animate(
                    ControlTemplate.TextColorProperty,
                    5,
                    EasingMode.EaseOutCubic,
                    AnimationInterpolators.Color)
                .Animate(
                    ControlTemplate.BorderColorProperty,
                    5,
                    EasingMode.EaseOutCubic,
                    AnimationInterpolators.Color)
                .Animate(
                    ControlTemplate.BorderThicknessPixelsProperty,
                    5,
                    EasingMode.EaseOutCubic,
                    AnimationInterpolators.Float);

            style.State(StyleState.Selected)
                .Set(ControlTemplate.BorderColorProperty, ThemeResources.AccentColor)
                .Set(ControlTemplate.BorderThicknessPixelsProperty, 2f)
                .Animate(
                    ControlTemplate.BorderColorProperty,
                    5,
                    EasingMode.EaseOutCubic,
                    AnimationInterpolators.Color)
                .Animate(
                    ControlTemplate.BorderThicknessPixelsProperty,
                    5,
                    EasingMode.EaseOutCubic,
                    AnimationInterpolators.Float);

            style.State(StyleState.Pressed)
                .Set(ControlTemplate.BorderColorProperty, ThemeResources.AccentColor)
                .Set(ControlTemplate.BorderThicknessPixelsProperty, 2f)
                .Set(ControlTemplate.RenderTransformProperty, new ScaleTransform(0.95f))
                .Animate(
                    ControlTemplate.RenderTransformProperty,
                    2,
                    EasingMode.EaseOutCubic,
                    AnimationInterpolators.RenderTransform)
                .Animate(
                    ControlTemplate.BackgroundColorProperty,
                    2,
                    EasingMode.EaseOutCubic,
                    AnimationInterpolators.Color)
                .Animate(
                    ControlTemplate.TextColorProperty,
                    2,
                    EasingMode.EaseOutCubic,
                    AnimationInterpolators.Color)
                .Animate(
                    ControlTemplate.BorderColorProperty,
                    2,
                    EasingMode.EaseOutCubic,
                    AnimationInterpolators.Color)
                .Animate(
                    ControlTemplate.BorderThicknessPixelsProperty,
                    2,
                    EasingMode.EaseOutCubic,
                    AnimationInterpolators.Float);
        }

        static void ConfigureScaleUpOnly(Style style)
        {
            style
                .Set(ControlTemplate.RenderTransformProperty, ScaleTransform.Identity)
                .Animate(
                    ControlTemplate.RenderTransformProperty,
                    6,
                    EasingMode.EaseOutCubic,
                    AnimationInterpolators.RenderTransform);

            style.State(StyleState.Hover)
                .Set(ControlTemplate.RenderTransformProperty, new ScaleTransform(1.05f))
                .Animate(
                    ControlTemplate.RenderTransformProperty,
                    5,
                    EasingMode.EaseOutCubic,
                    AnimationInterpolators.RenderTransform);

            style.State(StyleState.Pressed)
                .Set(ControlTemplate.RenderTransformProperty, new ScaleTransform(1.05f))
                .Animate(
                    ControlTemplate.RenderTransformProperty,
                    2,
                    EasingMode.EaseOutCubic,
                    AnimationInterpolators.RenderTransform);
        }
    }
}
