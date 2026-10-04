using Adk.Gui.Controls;
using Adk.Gui.Core.Invalidation;
using Adk.Gui.Core.PropertySystem;
using Adk.Gui.Styling;
using VRageMath;

namespace LcdMod.Client.Apps.Controls
{
    /// <summary>
    /// Reusable labeled percentage row for storage, gas, power, and similar
    /// block-property dashboards. Its visuals live entirely in the active style.
    /// </summary>
    internal sealed class BlockPropertyPercentage : Button
    {
        public static readonly StyledProperty<string> ItemIconProperty =
            StyledProperty.Register<BlockPropertyPercentage, string>(
                nameof(ItemIcon), "MissingIcon", UiInvalidation.Measure | UiInvalidation.Render);

        public static readonly StyledProperty<string> LabelProperty =
            StyledProperty.Register<BlockPropertyPercentage, string>(
                nameof(Label), string.Empty, UiInvalidation.Measure);

        public static readonly StyledProperty<float> ValueProperty =
            StyledProperty.Register<BlockPropertyPercentage, float>(
                nameof(Value), 0f, UiInvalidation.Arrange | UiInvalidation.Render);

        public static readonly StyledProperty<string> ValueTextProperty =
            StyledProperty.Register<BlockPropertyPercentage, string>(
                nameof(ValueText), string.Empty, UiInvalidation.Measure);
        public static readonly StyledProperty<string> FontFamilyProperty =
            StyledProperty.Register<BlockPropertyPercentage, string>(
                nameof(FontFamily), "White", UiInvalidation.Measure);

        public static readonly StyledProperty<BackgroundBrush> FillProperty =
            StyledProperty.Register<BlockPropertyPercentage, BackgroundBrush>(
                nameof(Fill), new SolidColorBrush(new Color(80, 160, 255)), UiInvalidation.Render);

        public static readonly StyledProperty<BackgroundBrush> TrackProperty =
            StyledProperty.Register<BlockPropertyPercentage, BackgroundBrush>(
                nameof(Track), new SolidColorBrush(new Color(82, 98, 107, 127)), UiInvalidation.Render);

        public string ItemIcon
        {
            get { return GetValue(ItemIconProperty); }
            set { SetValue(ItemIconProperty, string.IsNullOrWhiteSpace(value) ? "MissingIcon" : value); }
        }

        public string Label
        {
            get { return GetValue(LabelProperty); }
            set { SetValue(LabelProperty, value ?? string.Empty); }
        }

        public float Value
        {
            get { return GetValue(ValueProperty); }
            set { SetValue(ValueProperty, MathHelper.Clamp(value, 0f, 1f)); }
        }

        public string ValueText
        {
            get { return GetValue(ValueTextProperty); }
            set { SetValue(ValueTextProperty, value ?? string.Empty); }
        }

        public string FontFamily
        {
            get { return GetValue(FontFamilyProperty); }
            set { SetValue(FontFamilyProperty, string.IsNullOrWhiteSpace(value) ? "White" : value); }
        }

        public BackgroundBrush Fill
        {
            get { return GetValue(FillProperty); }
            set { SetValue(FillProperty, value); }
        }

        public BackgroundBrush Track
        {
            get { return GetValue(TrackProperty); }
            set { SetValue(TrackProperty, value); }
        }
    }
}
