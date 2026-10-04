using System;
using System.Collections.Generic;
using Adk.Gui.Animation;
using Adk.Gui.Controls;
using Adk.Gui.Core;
using Adk.Gui.Core.Invalidation;
using Adk.Gui.Core.PropertySystem;
using Adk.Gui.Layout;
using Adk.Gui.Rendering;
using VRageMath;

namespace LcdMod.Client.Apps.NpcMarket
{
    /// <summary>
    /// The market table header with its search. Children, in order: the search button, the search text box and the
    /// column headers. Closed, the button sits in front of the headers on one line; opening widens the text box from
    /// the button across the line while the headers drift down to a second line. Clicking the button opens the
    /// search and focuses the box; the box losing focus while empty closes it again.
    /// </summary>
    internal sealed class MarketSearchHeader : Panel
    {
        public static readonly StyledProperty<bool> IsOpenProperty =
            StyledProperty.Register<MarketSearchHeader, bool>(nameof(IsOpen), false);
        public static readonly StyledProperty<float> ExpansionProperty =
            StyledProperty.Register<MarketSearchHeader, float>(nameof(Expansion), 0f, UiInvalidation.Measure);
        public static readonly StyledProperty<float> ButtonWidthProperty =
            StyledProperty.Register<MarketSearchHeader, float>(nameof(ButtonWidth), 48f, UiInvalidation.Measure);
        public static readonly StyledProperty<float> LineHeightProperty =
            StyledProperty.Register<MarketSearchHeader, float>(nameof(LineHeight), 28f, UiInvalidation.Measure);

        const int AnimationFrames = 10;

        Button _button;
        TextBox _box;
        bool _boxWasFocused;

        public bool IsOpen
        {
            get { return GetValue(IsOpenProperty); }
            set { SetValue(IsOpenProperty, value); }
        }

        /// <summary>0 closed, 1 open; animated between the two.</summary>
        public float Expansion
        {
            get { return GetValue(ExpansionProperty); }
            set { SetValue(ExpansionProperty, value); }
        }

        public float ButtonWidth
        {
            get { return GetValue(ButtonWidthProperty); }
            set { SetValue(ButtonWidthProperty, value); }
        }

        public float LineHeight
        {
            get { return GetValue(LineHeightProperty); }
            set { SetValue(LineHeightProperty, value); }
        }

        protected override void OnPropertyChanged(IStyledProperty property)
        {
            base.OnPropertyChanged(property);
            if (property != IsOpenProperty)
                return;
            if (AnimationController == null)
                Expansion = IsOpen ? 1f : 0f;
            else
                this.Animate(ExpansionProperty, IsOpen ? 1f : 0f, AnimationFrames, AnimationInterpolators.Float,
                    EasingMode.EaseOutCubic);
            UpdateVisibility();
        }

        void Resolve()
        {
            IReadOnlyList<VisualElement> children = LogicalChildren;
            var button = children.Count > 0 ? children[0] as Button : null;
            if (!ReferenceEquals(button, _button))
            {
                _button = button;
                if (_button != null)
                    _button.Clicked = OpenAndFocus;
            }
            _box = children.Count > 1 ? children[1] as TextBox : null;
            UpdateVisibility();
        }

        void OpenAndFocus()
        {
            IsOpen = true;
            if (_box != null)
                _box.Focus();
        }

        void UpdateVisibility()
        {
            if (_button != null)
                _button.Visible = !IsOpen && Expansion < .5f;
            if (_box != null)
                _box.Visible = IsOpen || Expansion > 0f;
        }

        protected override Vector2 MeasureOverride(Vector2 availableSize)
        {
            Resolve();
            float width = float.IsPositiveInfinity(availableSize.X) ? 0f : availableSize.X;
            float line = LineHeight;
            IReadOnlyList<VisualElement> children = LogicalChildren;
            for (int i = 0; i < children.Count; i++)
                children[i].Measure(new Vector2(width, line));
            return new Vector2(width, line * (1f + MathHelper.Clamp(Expansion, 0f, 1f)));
        }

        protected override void ArrangeChildren(RectangleF bounds)
        {
            Resolve();
            float expansion = MathHelper.Clamp(Expansion, 0f, 1f);
            float line = LineHeight;
            float buttonWidth = Math.Min(ButtonWidth, bounds.Width);
            if (_button != null)
                _button.ArrangeInSlot(new RectangleF(bounds.X, bounds.Y, buttonWidth, line));
            if (_box != null)
                _box.Arrange(new RectangleF(bounds.X, bounds.Y, MathHelper.Lerp(buttonWidth, bounds.Width, expansion), line));
            IReadOnlyList<VisualElement> children = LogicalChildren;
            float headerX = MathHelper.Lerp(buttonWidth, 0f, expansion);
            for (int i = 2; i < children.Count; i++)
                children[i].ArrangeInSlot(new RectangleF(bounds.X + headerX, bounds.Y + line * expansion,
                    Math.Max(0f, bounds.Width - headerX), line));
        }

        protected override void DrawSelf(RenderContext context)
        {
            base.DrawSelf(context);
            bool focused = _box != null && _box.IsFocused;
            if (_boxWasFocused && !focused && _box.Text.Length == 0)
                IsOpen = false;
            _boxWasFocused = focused;
        }
    }
}
