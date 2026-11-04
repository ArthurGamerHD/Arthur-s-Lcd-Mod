using System;
using System.Collections.Generic;
using Acrux.Animation;
using Acrux.Controls;
using Acrux.Core;
using Acrux.Core.Invalidation;
using Acrux.Core.PropertySystem;
using Acrux.Layout;
using Acrux.Rendering;
using VRageMath;

namespace LcdMod.Client.Apps.Controls
{
    /// <summary>
    /// One line holding a collapsible search. Children, in order: the search button, the search text box and the
    /// content the search shares the line with. Closed, the button sits at the right end and the content fills the
    /// rest; opening widens the text box leftwards from the button while the content narrows. Clicking the button
    /// opens the search and focuses the box; the box losing focus while empty closes it again.
    /// </summary>
    internal sealed class SearchExpander : Panel
    {
        public static readonly StyledProperty<bool> IsOpenProperty =
            StyledProperty.Register<SearchExpander, bool>(nameof(IsOpen), false);
        public static readonly StyledProperty<float> ExpansionProperty =
            StyledProperty.Register<SearchExpander, float>(nameof(Expansion), 0f, UiInvalidation.Measure);
        public static readonly StyledProperty<float> ButtonWidthProperty =
            StyledProperty.Register<SearchExpander, float>(nameof(ButtonWidth), 36f, UiInvalidation.Measure);
        public static readonly StyledProperty<float> LineHeightProperty =
            StyledProperty.Register<SearchExpander, float>(nameof(LineHeight), 36f, UiInvalidation.Measure);
        public static readonly StyledProperty<float> OpenFractionProperty =
            StyledProperty.Register<SearchExpander, float>(nameof(OpenFraction), .6f, UiInvalidation.Measure);
        public static readonly StyledProperty<float> SpacingProperty =
            StyledProperty.Register<SearchExpander, float>(nameof(Spacing), 6f, UiInvalidation.Measure);

        const int AnimationFrames = 10;

        Button _button;
        TextBox _box;
        bool _boxWasFocused;

        public bool IsOpen
        {
            get { return GetValue(IsOpenProperty); }
            set { SetValue(IsOpenProperty, value); }
        }

        // 0 closed, 1 open; animated between the two.
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

        // Share of the line the open text box takes.
        public float OpenFraction
        {
            get { return GetValue(OpenFractionProperty); }
            set { SetValue(OpenFractionProperty, value); }
        }

        public float Spacing
        {
            get { return GetValue(SpacingProperty); }
            set { SetValue(SpacingProperty, value); }
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
            if (_box != null && !IsOpen && !string.IsNullOrEmpty(_box.Text))
                IsOpen = true;
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

        float SlotWidth(float width)
        {
            float closed = Math.Min(ButtonWidth, width);
            float open = Math.Max(closed, width * MathHelper.Clamp(OpenFraction, 0f, 1f));
            return MathHelper.Lerp(closed, open, MathHelper.Clamp(Expansion, 0f, 1f));
        }

        protected override Vector2 MeasureOverride(Vector2 availableSize)
        {
            Resolve();
            float width = float.IsPositiveInfinity(availableSize.X) ? 0f : availableSize.X;
            float line = LineHeight;
            float slot = SlotWidth(width);
            float content = Math.Max(0f, width - slot - Spacing);
            IReadOnlyList<VisualElement> children = LogicalChildren;
            for (int i = 0; i < children.Count; i++)
                children[i].Measure(new Vector2(i < 2 ? slot : content, line));
            return new Vector2(width, line);
        }

        protected override void ArrangeChildren(RectangleF bounds)
        {
            Resolve();
            float line = LineHeight;
            float slot = SlotWidth(bounds.Width);
            float buttonWidth = Math.Min(ButtonWidth, bounds.Width);
            if (_button != null)
                _button.ArrangeInSlot(new RectangleF(bounds.Right - buttonWidth, bounds.Y, buttonWidth, line));
            if (_box != null)
                _box.Arrange(new RectangleF(bounds.Right - slot, bounds.Y, slot, line));
            float content = Math.Max(0f, bounds.Width - slot - Spacing);
            IReadOnlyList<VisualElement> children = LogicalChildren;
            for (int i = 2; i < children.Count; i++)
                children[i].ArrangeInSlot(new RectangleF(bounds.X, bounds.Y, content, line));
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
