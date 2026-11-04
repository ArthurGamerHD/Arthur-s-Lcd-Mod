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
    internal sealed class InventoryItemControl : Button
    {
        public static readonly StyledProperty<string> ItemIconProperty =
            StyledProperty.Register<InventoryItemControl, string>(nameof(ItemIcon), "MissingIcon", UiInvalidation.Measure);
        public string ItemIcon { get { return GetValue(ItemIconProperty); } set { SetValue(ItemIconProperty, value); } }

        public static readonly StyledProperty<string> ItemNameProperty =
            StyledProperty.Register<InventoryItemControl, string>(nameof(ItemName), string.Empty, UiInvalidation.Measure);
        public string ItemName { get { return GetValue(ItemNameProperty); } set { SetValue(ItemNameProperty, value); } }

        public static readonly StyledProperty<string> AmountProperty =
            StyledProperty.Register<InventoryItemControl, string>(nameof(Amount), string.Empty, UiInvalidation.Measure);
        public string Amount { get { return GetValue(AmountProperty); } set { SetValue(AmountProperty, value); } }

        public static readonly StyledProperty<float> FontSizeProperty =
            StyledProperty.Register<InventoryItemControl, float>(nameof(FontSize), 1f, UiInvalidation.Measure);
        public float FontSize { get { return GetValue(FontSizeProperty); } set { SetValue(FontSizeProperty, value); } }

        public static readonly StyledProperty<string> FontFamilyProperty =
            StyledProperty.Register<InventoryItemControl, string>(nameof(FontFamily), "White", UiInvalidation.Measure);
        public string FontFamily { get { return GetValue(FontFamilyProperty); } set { SetValue(FontFamilyProperty, value); } }

        public static readonly StyledProperty<bool> TilesProperty =
            StyledProperty.Register<InventoryItemControl, bool>(nameof(Tiles), false, UiInvalidation.Measure | UiInvalidation.Style);
        public bool Tiles { get { return GetValue(TilesProperty); } set { SetValue(TilesProperty, value); } }

        public static readonly StyledProperty<bool> CardsProperty =
            StyledProperty.Register<InventoryItemControl, bool>(nameof(Cards), false, UiInvalidation.Measure | UiInvalidation.Style);
        public bool Cards { get { return GetValue(CardsProperty); } set { SetValue(CardsProperty, value); } }

        public static readonly StyledProperty<bool> TableProperty =
            StyledProperty.Register<InventoryItemControl, bool>(nameof(Table), false, UiInvalidation.Style);
        public bool Table { get { return GetValue(TableProperty); } set { SetValue(TableProperty, value); } }

        public static readonly StyledProperty<bool> OddProperty =
            StyledProperty.Register<InventoryItemControl, bool>(nameof(Odd), false, UiInvalidation.Style);
        public bool Odd { get { return GetValue(OddProperty); } set { SetValue(OddProperty, value); } }

        public static readonly StyledProperty<int> StatusProperty =
            StyledProperty.Register<InventoryItemControl, int>(nameof(Status), 0, UiInvalidation.Style);
        public int Status { get { return GetValue(StatusProperty); } set { SetValue(StatusProperty, value); } }

        public static readonly StyledProperty<string> FittedNameProperty =
            StyledProperty.Register<InventoryItemControl, string>(nameof(FittedName), string.Empty, UiInvalidation.Measure);
        public string FittedName { get { return GetValue(FittedNameProperty); } set { SetValue(FittedNameProperty, value); } }

        public static readonly StyledProperty<float> NameFontSizeProperty =
            StyledProperty.Register<InventoryItemControl, float>(nameof(NameFontSize), 1f, UiInvalidation.Measure);
        public float NameFontSize { get { return GetValue(NameFontSizeProperty); } set { SetValue(NameFontSizeProperty, value); } }

        public static readonly StyledProperty<float> AmountFontSizeProperty =
            StyledProperty.Register<InventoryItemControl, float>(nameof(AmountFontSize), 1f, UiInvalidation.Measure);
        public float AmountFontSize { get { return GetValue(AmountFontSizeProperty); } set { SetValue(AmountFontSizeProperty, value); } }

        public static readonly StyledProperty<float> IconSizeProperty =
            StyledProperty.Register<InventoryItemControl, float>(nameof(IconSize), 30f, UiInvalidation.Measure);
        public float IconSize { get { return GetValue(IconSizeProperty); } set { SetValue(IconSizeProperty, value); } }

        public static readonly StyledProperty<Vector4> IconMarginProperty =
            StyledProperty.Register<InventoryItemControl, Vector4>(nameof(IconMargin), Vector4.Zero, UiInvalidation.Measure);
        public Vector4 IconMargin { get { return GetValue(IconMarginProperty); } set { SetValue(IconMarginProperty, value); } }

        public static readonly StyledProperty<Vector4> NameMarginProperty =
            StyledProperty.Register<InventoryItemControl, Vector4>(nameof(NameMargin), Vector4.Zero, UiInvalidation.Measure);
        public Vector4 NameMargin { get { return GetValue(NameMarginProperty); } set { SetValue(NameMarginProperty, value); } }

        public static readonly StyledProperty<Vector4> AmountMarginProperty =
            StyledProperty.Register<InventoryItemControl, Vector4>(nameof(AmountMargin), Vector4.Zero, UiInvalidation.Measure);
        public Vector4 AmountMargin { get { return GetValue(AmountMarginProperty); } set { SetValue(AmountMarginProperty, value); } }

        public static readonly StyledProperty<Vector4> CardMarginProperty =
            StyledProperty.Register<InventoryItemControl, Vector4>(nameof(CardMargin), Vector4.Zero, UiInvalidation.Measure);
        public Vector4 CardMargin { get { return GetValue(CardMarginProperty); } set { SetValue(CardMarginProperty, value); } }

        public static readonly StyledProperty<Vector4> CardShadowMarginProperty =
            StyledProperty.Register<InventoryItemControl, Vector4>(nameof(CardShadowMargin), Vector4.Zero, UiInvalidation.Measure);
        public Vector4 CardShadowMargin { get { return GetValue(CardShadowMarginProperty); } set { SetValue(CardShadowMarginProperty, value); } }

        public static readonly StyledProperty<HorizontalAlignment> NameAlignmentProperty =
            StyledProperty.Register<InventoryItemControl, HorizontalAlignment>(nameof(NameAlignment), HorizontalAlignment.Left, UiInvalidation.Measure);
        public HorizontalAlignment NameAlignment { get { return GetValue(NameAlignmentProperty); } set { SetValue(NameAlignmentProperty, value); } }

        public static readonly StyledProperty<BackgroundBrush> CardShadowProperty =
            StyledProperty.Register<InventoryItemControl, BackgroundBrush>(nameof(CardShadow), new SolidColorBrush(Color.Transparent), UiInvalidation.Render);
        public BackgroundBrush CardShadow { get { return GetValue(CardShadowProperty); } set { SetValue(CardShadowProperty, value); } }

        Vector2 _measuredSize;
        float _measuredFont = -1f;
        string _measuredName, _measuredAmount, _measuredFamily;
        bool _measuredTiles;
        ITextMetrics _measuredMetrics;

        protected override void UpdatePseudoClasses()
        {
            base.UpdatePseudoClasses();
            PseudoClasses.Set(":card", Tiles && Cards);
            PseudoClasses.Set(":grid", Tiles && !Cards);
            PseudoClasses.Set(":table", Table);
            PseudoClasses.Set(":odd", Odd);
            PseudoClasses.Set(":warning", Status == 1);
            PseudoClasses.Set(":error", Status == 2);
        }

        protected override Vector2 MeasureOverride(Vector2 availableSize)
        {
            if (_measuredSize != availableSize || _measuredFont != FontSize ||
                _measuredName != ItemName || _measuredAmount != Amount ||
                _measuredFamily != FontFamily || _measuredTiles != Tiles ||
                !ReferenceEquals(_measuredMetrics, TextMetrics))
            {
                _measuredSize = availableSize; _measuredFont = FontSize;
                _measuredName = ItemName; _measuredAmount = Amount; _measuredFamily = FontFamily;
                _measuredTiles = Tiles; _measuredMetrics = TextMetrics;
                UpdateTextLayout(availableSize);
            }
            return TemplateRoot == null ? Vector2.Zero : TemplateRoot.Measure(availableSize);
        }

        void UpdateTextLayout(Vector2 size)
        {
            float font = Math.Max(.1f, FontSize);
            float padding = Tiles ? 15f * font : 0f;
            IconSize = Math.Min(Math.Max(0f, size.Y - padding * 2f), (Tiles ? 60f : 30f) * font);
            IconMargin = new Vector4(padding, padding, 0f, 0f);
            float left = padding + IconSize + 8f * font;
            float right = Tiles ? padding : 14f * font;
            float width = Math.Max(0f, size.X - left - right);
            float amountWidth = Tiles ? 0f : Math.Max(105f * font, Measure(Amount, font).X + 8f * font);
            FittedName = Trim(ItemName, Math.Max(0f, width - amountWidth), font);
            float lineHeight = Math.Max(1f, (size.Y - 2f * padding) * .5f);
            NameFontSize = Tiles ? Fit(FittedName, width, lineHeight, font) : font;
            AmountFontSize = Tiles ? Fit(Amount, width, lineHeight, font) : font;
            NameMargin = new Vector4(left, Tiles ? padding + lineHeight : 0f, right + amountWidth, 0f);
            AmountMargin = new Vector4(left, padding, right, 0f);
            NameAlignment = Tiles ? HorizontalAlignment.Right : HorizontalAlignment.Left;
            float inset = 7.5f * font;
            CardMargin = new Vector4(inset);
            CardShadowMargin = new Vector4(inset + 2f, inset + 2f, inset - 2f, inset - 2f);
        }

        Vector2 Measure(string text, float font) => TextMetrics == null ? Vector2.Zero : TextMetrics.Measure(FontFamily, text ?? string.Empty, font);

        string Trim(string text, float width, float font)
        {
            if (string.IsNullOrEmpty(text) || width <= 0f) return string.Empty;
            if (Measure(text, font).X <= width) return text;
            for (int length = text.Length - 1; length > 0; length--)
            {
                string candidate = text.Length <= length + 2 || length < 5
                    ? text.Substring(0, length - 1) + "…"
                    : text.Substring(0, length - 4) + "…" + text.Substring(text.Length - 3);
                if (Measure(candidate, font).X <= width) return candidate;
            }
            return string.Empty;
        }

        float Fit(string text, float width, float height, float font)
        {
            Vector2 size = Measure(text, font);
            return size.X <= 0f || size.Y <= 0f ? font : font * Math.Min(1f, Math.Min(width / size.X, height / size.Y));
        }

        protected override void DrawSelf(RenderContext context)
        {
            Color background = Background == null ? Color.Transparent : Background.FallbackColor;
            Color shadow = new Color(background.R / 5, background.G / 5, background.B / 5, background.A);
            if (CardShadow.FallbackColor != shadow) CardShadow = new SolidColorBrush(shadow);
            base.DrawSelf(context);
        }

        protected override IDisposable BeginChildDraw(RenderContext context, VisualElement child) => context.PushClip(Bounds);
    }
}
