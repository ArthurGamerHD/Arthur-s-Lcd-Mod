using Adk.Gui.Controls;
using Adk.Gui.Core.Invalidation;
using Adk.Gui.Core.PropertySystem;
using LcdMod.Client.Apps.ViewModel;
using LcdMod.Common.Config.Components;

namespace LcdMod.Client.Apps.Controls
{
    /// <summary>Reusable item icon/name/amount presentation for inventory-like apps.</summary>
    internal sealed class ItemDisplay : Button
    {
        public static readonly StyledProperty<string> ItemIconProperty =
            StyledProperty.Register<ItemDisplay, string>(
                nameof(ItemIcon), "MissingIcon", UiInvalidation.Measure | UiInvalidation.Render);
        public static readonly StyledProperty<string> LabelProperty =
            StyledProperty.Register<ItemDisplay, string>(
                nameof(Label), string.Empty, UiInvalidation.Measure);
        public static readonly StyledProperty<string> PrimaryTextProperty =
            StyledProperty.Register<ItemDisplay, string>(
                nameof(PrimaryText), string.Empty, UiInvalidation.Measure);
        public static readonly StyledProperty<string> SecondaryTextProperty =
            StyledProperty.Register<ItemDisplay, string>(
                nameof(SecondaryText), string.Empty, UiInvalidation.Measure);
        public static readonly StyledProperty<string> FontFamilyProperty =
            StyledProperty.Register<ItemDisplay, string>(
                nameof(FontFamily), "White", UiInvalidation.Measure);
        public static readonly StyledProperty<ItemDisplayMode> DisplayModeProperty =
            StyledProperty.Register<ItemDisplay, ItemDisplayMode>(
                nameof(DisplayMode), ItemDisplayMode.List, UiInvalidation.Style | UiInvalidation.Render);
        public static readonly StyledProperty<ItemAvailabilityStatus> AvailabilityStatusProperty =
            StyledProperty.Register<ItemDisplay, ItemAvailabilityStatus>(
                nameof(AvailabilityStatus), ItemAvailabilityStatus.Normal,
                UiInvalidation.Style | UiInvalidation.Render);

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

        public string PrimaryText
        {
            get { return GetValue(PrimaryTextProperty); }
            set { SetValue(PrimaryTextProperty, value ?? string.Empty); }
        }

        public string SecondaryText
        {
            get { return GetValue(SecondaryTextProperty); }
            set { SetValue(SecondaryTextProperty, value ?? string.Empty); }
        }

        public string FontFamily
        {
            get { return GetValue(FontFamilyProperty); }
            set { SetValue(FontFamilyProperty, string.IsNullOrWhiteSpace(value) ? "White" : value); }
        }

        public ItemDisplayMode DisplayMode
        {
            get { return GetValue(DisplayModeProperty); }
            set { SetValue(DisplayModeProperty, value); }
        }

        public ItemAvailabilityStatus AvailabilityStatus
        {
            get { return GetValue(AvailabilityStatusProperty); }
            set { SetValue(AvailabilityStatusProperty, value); }
        }

        protected override void UpdatePseudoClasses()
        {
            base.UpdatePseudoClasses();
            PseudoClasses.Set("card", DisplayMode == ItemDisplayMode.Card);
            PseudoClasses.Set("grid", DisplayMode == ItemDisplayMode.Grid);
            PseudoClasses.Set("list", DisplayMode == ItemDisplayMode.List);
            PseudoClasses.Set("table", DisplayMode == ItemDisplayMode.Table);
            PseudoClasses.Set("warning", AvailabilityStatus == ItemAvailabilityStatus.Warning);
            PseudoClasses.Set("error", AvailabilityStatus == ItemAvailabilityStatus.Error);
        }
    }
}
