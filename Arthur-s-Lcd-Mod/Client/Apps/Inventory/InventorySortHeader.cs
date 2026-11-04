using System;
using Acrux.Controls;
using Acrux.Core;
using Acrux.Core.Invalidation;
using Acrux.Core.PropertySystem;
using Acrux.Rendering;
using Acrux.Styling;
using VRageMath;

namespace LcdMod.Client.Apps.Inventory
{
    internal sealed class InventorySortHeader : Button
    {
        public static readonly StyledProperty<string> CaptionProperty =
            StyledProperty.Register<InventorySortHeader, string>(nameof(Caption), string.Empty, UiInvalidation.Measure);
        public string Caption { get { return GetValue(CaptionProperty); } set { SetValue(CaptionProperty, value); } }

        public static readonly StyledProperty<string> DisplayCaptionProperty =
            StyledProperty.Register<InventorySortHeader, string>(nameof(DisplayCaption), string.Empty, UiInvalidation.Measure);
        public string DisplayCaption { get { return GetValue(DisplayCaptionProperty); } set { SetValue(DisplayCaptionProperty, value); } }

        public static readonly StyledProperty<string> FontFamilyProperty =
            StyledProperty.Register<InventorySortHeader, string>(nameof(FontFamily), "White", UiInvalidation.Measure);
        public string FontFamily { get { return GetValue(FontFamilyProperty); } set { SetValue(FontFamilyProperty, value); } }

        public static readonly StyledProperty<float> FontSizeProperty =
            StyledProperty.Register<InventorySortHeader, float>(nameof(FontSize), 1f, UiInvalidation.Measure);
        public float FontSize { get { return GetValue(FontSizeProperty); } set { SetValue(FontSizeProperty, value); } }

        public static readonly StyledProperty<bool> IsAmountProperty =
            StyledProperty.Register<InventorySortHeader, bool>(nameof(IsAmount), false, UiInvalidation.Measure);
        public bool IsAmount { get { return GetValue(IsAmountProperty); } set { SetValue(IsAmountProperty, value); } }

        public static readonly StyledProperty<bool> ActiveProperty =
            StyledProperty.Register<InventorySortHeader, bool>(nameof(Active), false, UiInvalidation.Measure | UiInvalidation.Style);
        public bool Active { get { return GetValue(ActiveProperty); } set { SetValue(ActiveProperty, value); } }

        public static readonly StyledProperty<bool> DescendingProperty =
            StyledProperty.Register<InventorySortHeader, bool>(nameof(Descending), false, UiInvalidation.Measure);
        public bool Descending { get { return GetValue(DescendingProperty); } set { SetValue(DescendingProperty, value); } }

        public static readonly StyledProperty<Vector4> CaptionMarginProperty =
            StyledProperty.Register<InventorySortHeader, Vector4>(nameof(CaptionMargin), Vector4.Zero, UiInvalidation.Measure);
        public Vector4 CaptionMargin { get { return GetValue(CaptionMarginProperty); } set { SetValue(CaptionMarginProperty, value); } }

        public static readonly StyledProperty<Vector4> IndicatorMarginProperty =
            StyledProperty.Register<InventorySortHeader, Vector4>(nameof(IndicatorMargin), Vector4.Zero, UiInvalidation.Measure);
        public Vector4 IndicatorMargin { get { return GetValue(IndicatorMarginProperty); } set { SetValue(IndicatorMarginProperty, value); } }

        public static readonly StyledProperty<float> IndicatorSizeProperty =
            StyledProperty.Register<InventorySortHeader, float>(nameof(IndicatorSize), 6f, UiInvalidation.Measure);
        public float IndicatorSize { get { return GetValue(IndicatorSizeProperty); } set { SetValue(IndicatorSizeProperty, value); } }

        public static readonly StyledProperty<float> IndicatorWidthProperty =
            StyledProperty.Register<InventorySortHeader, float>(nameof(IndicatorWidth), 8f, UiInvalidation.Measure);
        public float IndicatorWidth { get { return GetValue(IndicatorWidthProperty); } set { SetValue(IndicatorWidthProperty, value); } }

        static readonly Transform Downward = new RotateTransform(180f);
        public static readonly StyledProperty<Transform> IndicatorTransformProperty =
            StyledProperty.Register<InventorySortHeader, Transform>(nameof(IndicatorTransform), null, UiInvalidation.Render);
        public Transform IndicatorTransform { get { return GetValue(IndicatorTransformProperty); } set { SetValue(IndicatorTransformProperty, value); } }

        public static readonly StyledProperty<HorizontalAlignment> CaptionAlignmentProperty =
            StyledProperty.Register<InventorySortHeader, HorizontalAlignment>(nameof(CaptionAlignment), HorizontalAlignment.Left, UiInvalidation.Measure);
        public HorizontalAlignment CaptionAlignment { get { return GetValue(CaptionAlignmentProperty); } set { SetValue(CaptionAlignmentProperty, value); } }

        Vector2 _measuredSize;
        float _measuredFont = -1f;
        string _measuredCaption, _measuredFamily;
        bool _measuredActive, _measuredAmount;
        ITextMetrics _measuredMetrics;

        protected override void UpdatePseudoClasses()
        {
            base.UpdatePseudoClasses();
            PseudoClasses.Set(":active", Active);
        }

        protected override Vector2 MeasureOverride(Vector2 availableSize)
        {
            if (_measuredSize != availableSize || _measuredFont != FontSize ||
                _measuredCaption != Caption || _measuredFamily != FontFamily ||
                _measuredActive != Active || _measuredAmount != IsAmount ||
                !ReferenceEquals(_measuredMetrics, TextMetrics))
            {
                _measuredSize = availableSize; _measuredFont = FontSize;
                _measuredCaption = Caption; _measuredFamily = FontFamily;
                _measuredActive = Active; _measuredAmount = IsAmount; _measuredMetrics = TextMetrics;
                UpdateCaptionLayout(availableSize);
            }
            IndicatorTransform = Descending ? Downward : null;
            return TemplateRoot == null ? Vector2.Zero : TemplateRoot.Measure(availableSize);
        }

        void UpdateCaptionLayout(Vector2 availableSize)
        {
            float font = Math.Max(.1f, FontSize);
            float width = Math.Max(0f, availableSize.X - 16f * font - (Active ? 18f * font : 0f));
            string original = Caption ?? string.Empty;
            string text = original;
            int length = original.Length;
            while (length > 0 && TextMetrics != null && TextMetrics.Measure(FontFamily, text, .58f * font).X > width)
            {
                length--;
                text = length == 0 ? string.Empty : original.Length <= length + 2 || length < 5
                    ? original.Substring(0, length - 1) + "…"
                    : original.Substring(0, length - 4) + "…" + original.Substring(original.Length - 3);
            }
            DisplayCaption = text;
            CaptionAlignment = IsAmount ? HorizontalAlignment.Right : HorizontalAlignment.Left;
            CaptionMargin = IsAmount ? new Vector4(0f, 0f, 14f * font, 0f) : new Vector4(8f * font, 0f, 0f, 0f);
            Vector2 size = TextMetrics == null ? Vector2.Zero : TextMetrics.Measure(FontFamily, text, .58f * font);
            float x = IsAmount ? availableSize.X - 14f * font - size.X - 8f * font : 8f * font + size.X + 8f * font;
            IndicatorSize = 6f * font;
            IndicatorWidth = 8f * font;
            IndicatorMargin = new Vector4(Math.Max(0f, x - IndicatorWidth / 2f), 0f, 0f, 0f);
        }
    }
}
