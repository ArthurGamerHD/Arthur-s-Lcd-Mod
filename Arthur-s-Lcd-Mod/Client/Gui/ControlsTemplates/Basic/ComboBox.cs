using System;
using System.Collections.Generic;
using LcdMod.Client.Gui.ControlsTemplates.Panels;
using LcdMod.Client.Gui.Styling;
using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace LcdMod.Client.Gui.ControlsTemplates.Basic
{
    public enum ComboBoxOpenDirection
    {
        Down,
        Up
    }

    public sealed class ComboBox<T> : Button
    {
        readonly List<T> _options = new List<T>();
        readonly List<Button> _optionButtons = new List<Button>();
        readonly Func<T, string> _getLabel;
        readonly Action<T> _selectionChanged;
        readonly Action _stateChanged;
        T _selectedValue;
        float _layoutScale = 1f;

        public ComboBox(IEnumerable<T> options, Func<T, string> getLabel, Action<T> selectionChanged,
            Action stateChanged = null)
            : base(default(RectangleF), CursorType.Hand, null, null)
        {
            _getLabel = getLabel ?? (value => value == null ? string.Empty : value.ToString());
            _selectionChanged = selectionChanged;
            _stateChanged = stateChanged;
            SetOnClick(OnComboClicked);
            SetOptions(options);
        }

        public ComboBoxOpenDirection OpenDirection { get; set; } = ComboBoxOpenDirection.Down;
        public float OptionGapPixels { get; set; } = 2f;
        public bool IsOpen { get; private set; }
        public T SelectedValue => _selectedValue;

        public void Configure(RectangleF bounds, float scale)
        {
            _layoutScale = Math.Max(0.01f, scale);
            SetRect(bounds);
            ArrangeOptionButtons();
            SetVisible(true);
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
            if (notify)
                _selectionChanged?.Invoke(value);
        }

        public void Close()
        {
            if (!IsOpen)
                return;

            IsOpen = false;
            SetOptionVisibility();
            MarkDirty();
            _stateChanged?.Invoke();
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
            RenderButton(this, Bounds, GetLabel(_selectedValue), true, false, sprites);
        }

        protected override StyleState GetStyleState()
        {
            var state = base.GetStyleState();

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

            for (var i = 0; i < _optionButtons.Count; i++)
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
            _stateChanged?.Invoke();
        }

        void OnOptionClicked(ButtonModel model, object sender)
        {
            var option = model as ComboBoxOptionModel<T>;
            if (option == null)
                return;

            var changed = !EqualityComparer<T>.Default.Equals(_selectedValue, option.Value);
            _selectedValue = option.Value;
            IsOpen = false;
            SetOptionVisibility();
            MarkDirty();
            if (changed)
                _selectionChanged?.Invoke(_selectedValue);
            else
                _stateChanged?.Invoke();
        }

        void EnsureOptionButtons()
        {
            while (_optionButtons.Count < _options.Count)
            {
                var button = new Button(default(RectangleF), new ComboBoxOptionModel<T> { Clicked = OnOptionClicked });
                button.CustomRender = RenderOptionButton;
                AddChild(button);
                _optionButtons.Add(button);
            }

            for (var i = 0; i < _optionButtons.Count; i++)
            {
                var visible = IsOpen && i < _options.Count;
                _optionButtons[i].SetVisible(visible);
            }
        }

        void ArrangeOptionButtons()
        {
            EnsureOptionButtons();
            var gap = OptionGapPixels * _layoutScale;
            for (var i = 0; i < _optionButtons.Count; i++)
            {
                var button = _optionButtons[i];
                if (i >= _options.Count)
                {
                    button.SetVisible(false);
                    continue;
                }

                var model = button.DataContext as ComboBoxOptionModel<T>;
                model.Value = _options[i];
                model.Text = GetLabel(_options[i]);
                model.Enabled = true;
                model.Clicked = OnOptionClicked;

                var offset = (i + 1) * Bounds.Height + (i + 1) * gap;
                var y = OpenDirection == ComboBoxOpenDirection.Up ? Bounds.Y - offset : Bounds.Y + offset;
                button.SetRect(new RectangleF(Bounds.X, y, Bounds.Width, Bounds.Height));
                button.SetCursor(CursorType.Hand);
                button.CustomRender = RenderOptionButton;
            }

            SetOptionVisibility();
        }

        void SetOptionVisibility()
        {
            for (var i = 0; i < _optionButtons.Count; i++)
                _optionButtons[i].SetVisible(Enabled && IsOpen && i < _options.Count);
        }

        void RenderOptionButton(ControlTemplate control, List<MySprite> sprites)
        {
            UpdateLayoutScale(LayoutScale);

            var model = control.DataContext as ComboBoxOptionModel<T>;
            var selected = model != null && EqualityComparer<T>.Default.Equals(_selectedValue, model.Value);
            RenderButton(control, control.Bounds, model != null ? model.Text : string.Empty, false, selected, sprites);
        }

        void RenderButton(ControlTemplate control, RectangleF rect, string text, bool drawArrow, bool selected,
            List<MySprite> sprites)
        {
            var scale = Math.Max(0.01f, _layoutScale);
            var panelColor = control != null ? control.BackgroundColor : BackgroundColor;
            var textColor = control != null ? control.TextColor : TextColor;

            if (selected)
            {
                panelColor = GetResourceColor(ThemeResources.AccentColor, panelColor);
                textColor = GetResourceColor(ThemeResources.OnAccentColor, textColor);
            }

            var textScale = 0.58f * scale * (control != null ? control.FontScale : FontScale);
            var fontId = control != null ? control.TextFont : TextFont;
            var borderRadius = control != null ? control.GetEffectiveRenderBorderRadiusPixels() : GetEffectiveRenderBorderRadiusPixels();

            BorderRenderer.CreateSpritesFromRect(rect, sprites, panelColor,
                radiusPixels: borderRadius,
                radiusScale: scale);
            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT,
                Data = text ?? string.Empty,
                Position = new Vector2(rect.X + 8f * scale,
                    rect.Center.Y - MeasureText(text ?? string.Empty, fontId, textScale).Y * 0.5f),
                RotationOrScale = textScale,
                Color = textColor,
                Alignment = TextAlignment.LEFT,
                FontId = fontId
            });

            if (!drawArrow)
                return;

            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXTURE,
                Data = "Triangle",
                Position = new Vector2(rect.Right - 10f * scale, rect.Center.Y),
                Size = new Vector2(8f * scale, 6f * scale),
                RotationOrScale = IsOpen ? MathHelper.Pi : 0f,
                Color = textColor,
                Alignment = TextAlignment.CENTER
            });
        }

        void UpdateLayoutScale(float scale)
        {
            var safeScale = Math.Max(0.01f, scale);
            if (Math.Abs(_layoutScale - safeScale) <= 0.0001f)
                return;

            _layoutScale = safeScale;
            ArrangeOptionButtons();
        }

        string GetLabel(T value)
        {
            return _getLabel(value) ?? string.Empty;
        }

        sealed class ComboBoxOptionModel<TValue> : ButtonModel
        {
            public TValue Value { get; set; }
        }
    }
}
