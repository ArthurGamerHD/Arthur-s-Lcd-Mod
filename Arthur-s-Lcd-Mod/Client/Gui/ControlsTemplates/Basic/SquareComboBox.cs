using System;
using System.Collections.Generic;
using LcdMod.Client.Gui.ControlsTemplates.Panels;
using LcdMod.Client.Gui.Styling;
using LcdMod.Client.Gui.Tooltip;
using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace LcdMod.Client.Gui.ControlsTemplates.Basic
{
    public enum SquareComboBoxOpenDirection
    {
        Left,
        Right
    }

    /// <summary>
    /// Compact map-tool style selector. The selected value occupies one square
    /// button and the options open as a horizontal row of square buttons.
    /// </summary>
    public sealed class SquareComboBox<T> : Button
    {
        readonly List<T> _options = new List<T>();
        readonly List<Button> _optionButtons = new List<Button>();
        readonly Func<T, string> _getLabel;
        readonly Func<T, string> _getGlyph;
        readonly Func<T, InteractiveTooltip> _getTooltip;
        readonly Action<ControlTemplate, RectangleF, T, Color, List<MySprite>> _renderContent;
        readonly Action<T> _selectionChanged;
        readonly Action _stateChanged;
        T _selectedValue;
        float _layoutScale = 1f;

        public SquareComboBox(
            IEnumerable<T> options,
            Func<T, string> getLabel,
            Func<T, string> getGlyph,
            Action<T> selectionChanged,
            Action stateChanged = null,
            Func<T, InteractiveTooltip> getTooltip = null,
            Action<ControlTemplate, RectangleF, T, Color, List<MySprite>> renderContent = null)
            : base(default(RectangleF), CursorType.Hand)
        {
            _getLabel = getLabel ?? (value => value == null ? string.Empty : value.ToString());
            _getGlyph = getGlyph ?? _getLabel;
            _selectionChanged = selectionChanged;
            _stateChanged = stateChanged;
            _getTooltip = getTooltip;
            _renderContent = renderContent;
            SetOnClick(OnComboClicked);
            SetOptions(options);
        }

        public SquareComboBoxOpenDirection OpenDirection { get; set; } =
            SquareComboBoxOpenDirection.Left;

        public float OptionGapPixels { get; set; } = 6f;

        public TooltipPlacement PreferredTooltipPlacement { get; private set; } = TooltipPlacement.Auto;

        public bool AnchorTooltipsToExpandedBounds { get; private set; }

        public bool IsOpen { get; private set; }

        public T SelectedValue
        {
            get { return _selectedValue; }
        }

        public void Configure(RectangleF bounds, float scale)
        {
            _layoutScale = Math.Max(0.01f, scale);
            SetRect(bounds);
            ArrangeOptionButtons();
            SetVisible(true);
        }

        public void ConfigureTooltips(
            TooltipPlacement placement,
            bool anchorToExpandedBounds)
        {
            PreferredTooltipPlacement = placement;
            AnchorTooltipsToExpandedBounds = anchorToExpandedBounds;
            ArrangeOptionButtons();
        }

        public InteractiveTooltip PrepareTooltip(InteractiveTooltip tooltip)
        {
            if (tooltip == null)
                return null;

            tooltip.Placement = PreferredTooltipPlacement;
            tooltip.AnchorBoundsGetter = AnchorTooltipsToExpandedBounds
                ? (Func<ControlTemplate, RectangleF>)(delegate { return GetExpandedBounds(); })
                : null;
            return tooltip;
        }

        public RectangleF GetExpandedBounds()
        {
            RectangleF expanded = Bounds;
            if (!IsOpen)
                return expanded;

            for (int i = 0; i < _optionButtons.Count && i < _options.Count; i++)
            {
                RectangleF optionBounds = _optionButtons[i].Bounds;
                float left = Math.Min(expanded.X, optionBounds.X);
                float top = Math.Min(expanded.Y, optionBounds.Y);
                float right = Math.Max(expanded.Right, optionBounds.Right);
                float bottom = Math.Max(expanded.Bottom, optionBounds.Bottom);
                expanded = new RectangleF(left, top, right - left, bottom - top);
            }

            return expanded;
        }

        public void SetOptions(IEnumerable<T> options)
        {
            _options.Clear();
            if (options != null)
                _options.AddRange(options);

            EnsureOptionButtons();
            ArrangeOptionButtons();
            MarkDirty();
        }

        public void SetSelectedValue(T value, bool notify = false)
        {
            if (EqualityComparer<T>.Default.Equals(_selectedValue, value))
                return;

            _selectedValue = value;
            MarkDirty();
            if (notify && _selectionChanged != null)
                _selectionChanged(value);
        }

        public void Close()
        {
            if (!IsOpen)
                return;

            IsOpen = false;
            SetOptionVisibility();
            MarkDirty();
            if (_stateChanged != null)
                _stateChanged();
        }

        protected override void OnEnabledChanged()
        {
            if (!Enabled)
                IsOpen = false;

            SetOptionVisibility();
        }

        public override void SetRect(RectangleF bounds)
        {
            base.SetRect(bounds);
            ArrangeOptionButtons();
        }

        protected override void RenderDefault(List<MySprite> sprites)
        {
            UpdateLayoutScale(LayoutScale);
            RenderSquare(this, Bounds, _selectedValue, false, true, sprites);
        }

        protected override StyleState GetStyleState()
        {
            StyleState state = base.GetStyleState();
            if (IsOpen)
                state |= StyleState.Opened;
            return state;
        }

        protected override bool CanResolveChildren(Vector2 point, bool selfHit)
        {
            return Enabled && (selfHit || IsOpen);
        }

        public override void AddOverlayEntries(List<Control> entries)
        {
            if (!Visible || entries == null || !IsOpen)
                return;

            for (int i = 0; i < _optionButtons.Count; i++)
            {
                if (_optionButtons[i].Visible)
                    entries.Add(_optionButtons[i]);
            }
        }

        void OnComboClicked(object dataContext, object sender)
        {
            if (!Enabled)
                return;

            IsOpen = !IsOpen;
            SetOptionVisibility();
            MarkDirty();
            if (_stateChanged != null)
                _stateChanged();
        }

        void OnOptionClicked(ButtonModel model, object sender)
        {
            var option = model as SquareComboBoxOptionModel<T>;
            if (option == null)
                return;

            bool changed = !EqualityComparer<T>.Default.Equals(_selectedValue, option.Value);
            _selectedValue = option.Value;
            IsOpen = false;
            SetOptionVisibility();
            MarkDirty();

            if (changed && _selectionChanged != null)
                _selectionChanged(_selectedValue);
            else if (_stateChanged != null)
                _stateChanged();
        }

        void EnsureOptionButtons()
        {
            while (_optionButtons.Count < _options.Count)
            {
                var button = new Button(
                    default(RectangleF),
                    new SquareComboBoxOptionModel<T> { Clicked = OnOptionClicked });
                button.BorderThicknessPixels = 0f;
                button.CustomRender = RenderOptionButton;
                AddChild(button);
                _optionButtons.Add(button);
            }
        }

        void ArrangeOptionButtons()
        {
            EnsureOptionButtons();
            float gap = OptionGapPixels * _layoutScale;

            for (int i = 0; i < _optionButtons.Count; i++)
            {
                Button button = _optionButtons[i];
                if (i >= _options.Count)
                {
                    button.SetVisible(false);
                    continue;
                }

                T value = _options[i];
                var model = button.DataContext as SquareComboBoxOptionModel<T>;
                if (model != null)
                {
                    model.Value = value;
                    model.Text = GetLabel(value);
                    model.Enabled = true;
                    model.Clicked = OnOptionClicked;
                }

                float offset = (i + 1) * (Bounds.Width + gap);
                float x = OpenDirection == SquareComboBoxOpenDirection.Left
                    ? Bounds.X - offset
                    : Bounds.X + offset;
                button.SetRect(new RectangleF(x, Bounds.Y, Bounds.Width, Bounds.Height));
                button.SetCursor(CursorType.Hand);
                button.CustomRender = RenderOptionButton;
                button.SetTooltip(PrepareTooltip(_getTooltip != null ? _getTooltip(value) : null));
            }

            SetOptionVisibility();
        }

        void SetOptionVisibility()
        {
            for (int i = 0; i < _optionButtons.Count; i++)
                _optionButtons[i].SetVisible(Enabled && IsOpen && i < _options.Count);
        }

        void RenderOptionButton(ControlTemplate control, List<MySprite> sprites)
        {
            UpdateLayoutScale(LayoutScale);

            var model = control.DataContext as SquareComboBoxOptionModel<T>;
            T value = model != null ? model.Value : default(T);
            bool selected = model != null &&
                            EqualityComparer<T>.Default.Equals(_selectedValue, model.Value);
            RenderSquare(control, control.Bounds, value, false, selected, sprites);
        }

        void RenderSquare(
            ControlTemplate control,
            RectangleF rect,
            T value,
            bool drawDirectionIndicator,
            bool selected,
            List<MySprite> sprites)
        {
            float scale = Math.Max(0.01f, _layoutScale);
            Color panelColor = selected
                ? control.GetResourceColor(ThemeResources.AccentContainerColor, control.BackgroundColor)
                : control.GetResourceColor(ThemeResources.SurfaceContainerHighColor, control.BackgroundColor);
            Color textColor = selected
                ? control.GetResourceColor(ThemeResources.OnAccentContainerColor, control.TextColor)
                : control.GetResourceColor(ThemeResources.OnSurfaceColor, control.TextColor);

            if (control.IsPointerOver)
            {
                panelColor = selected
                    ? control.GetResourceColor(ThemeResources.AccentColor, panelColor)
                    : control.GetResourceColor(ThemeResources.SurfaceContainerHighestColor, panelColor);
                textColor = selected
                    ? control.GetResourceColor(ThemeResources.OnAccentColor, textColor)
                    : textColor;
            }

            if (control.IsPressed)
            {
                panelColor = control.GetResourceColor(ThemeResources.SecondaryContainerColor, panelColor);
                textColor = control.GetResourceColor(ThemeResources.OnSecondaryContainerColor, textColor);
            }

            Color shadow = control.GetResourceColor(
                ThemeResources.ShadowColor,
                new Color(0, 0, 0, 150));
            float shadowOffset = Math.Max(1f, 2f * scale);
            RectangleF shadowRect = new RectangleF(
                rect.X + shadowOffset,
                rect.Y + shadowOffset,
                rect.Width,
                rect.Height);

            BorderRenderer.CreateSpritesFromRect(
                shadowRect,
                sprites,
                shadow,
                radiusPixels: control.GetEffectiveRenderBorderRadiusPixels(),
                radiusScale: scale);
            BorderRenderer.CreateSpritesFromRect(
                rect,
                sprites,
                panelColor,
                radiusPixels: control.GetEffectiveRenderBorderRadiusPixels(),
                radiusScale: scale);

            if (_renderContent != null)
            {
                float previewInsetX = rect.Width * 0.1f;
                float previewInsetY = rect.Height * 0.1f;
                var previewRect = new RectangleF(
                    rect.X + previewInsetX,
                    rect.Y + previewInsetY,
                    Math.Max(1f, rect.Width - previewInsetX * 2f),
                    Math.Max(1f, rect.Height - previewInsetY * 2f));
                _renderContent(control, previewRect, value, textColor, sprites);
            }
            else
            {
                string glyph = GetGlyph(value);
                float textScale = 0.48f * scale * control.FontScale;
                Vector2 textSize = control.MeasureText(glyph, control.TextFont, textScale);
                sprites.Add(new MySprite
                {
                    Type = SpriteType.TEXT,
                    Data = glyph,
                    Position = new Vector2(rect.Center.X, rect.Center.Y - textSize.Y * 0.5f),
                    RotationOrScale = textScale,
                    Color = textColor,
                    Alignment = TextAlignment.CENTER,
                    FontId = control.TextFont
                });
            }

            if (!drawDirectionIndicator)
                return;

            float indicatorSize = Math.Max(4f, rect.Width * 0.13f);
            float inset = Math.Max(5f, rect.Width * 0.16f);
            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXTURE,
                Data = "Triangle",
                Position = new Vector2(rect.Right - inset, rect.Bottom - inset),
                Size = new Vector2(indicatorSize, indicatorSize * 0.8f),
                RotationOrScale = OpenDirection == SquareComboBoxOpenDirection.Left
                    ? -MathHelper.PiOver2
                    : MathHelper.PiOver2,
                Color = textColor,
                Alignment = TextAlignment.CENTER
            });
        }

        void UpdateLayoutScale(float scale)
        {
            float safeScale = Math.Max(0.01f, scale);
            if (Math.Abs(_layoutScale - safeScale) <= 0.0001f)
                return;

            _layoutScale = safeScale;
            ArrangeOptionButtons();
        }

        string GetLabel(T value)
        {
            return _getLabel(value) ?? string.Empty;
        }

        string GetGlyph(T value)
        {
            return _getGlyph(value) ?? string.Empty;
        }

        sealed class SquareComboBoxOptionModel<TValue> : ButtonModel
        {
            public TValue Value { get; set; }
        }
    }
}
