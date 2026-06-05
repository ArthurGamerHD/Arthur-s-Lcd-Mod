using System;
using System.Collections.Generic;
using LcdMod.Client.Gui.ControlsTemplates.Panels;
using LcdMod.Client.Helpers;
using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace LcdMod.Client.Gui.ControlsTemplates.Basic
{
    public enum ComboBoxOpenDirection
    {
        Down,
        Up
    }

    public sealed class ComboBox<T> : RectangleControl
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

        public void Configure(RectangleF bounds, float scale, ControlStyle style)
        {
            _layoutScale = Math.Max(0f, scale);
            SetRect(bounds);
            SetStyle(style);
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

        public override void SetRect(RectangleF bounds)
        {
            base.SetRect(bounds);
            ArrangeOptionButtons();
        }

        protected override void RenderDefault(ControlRenderContext context, List<MySprite> sprites)
        {
            RenderButton(Bounds, GetLabel(_selectedValue), true, false, context, sprites);
            if (!IsOpen)
                return;

            for (var i = 0; i < _optionButtons.Count; i++)
                _optionButtons[i].Render(context, sprites);
        }

        protected override bool CanResolveChildren(Vector2 point, bool selfHit)
        {
            return selfHit || IsOpen;
        }

        void OnComboClicked(object dataContext, object sender)
        {
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
                button.SetStyle(Style);
                button.SetCursor(CursorType.Hand);
                button.CustomRender = RenderOptionButton;
            }

            SetOptionVisibility();
        }

        void SetOptionVisibility()
        {
            for (var i = 0; i < _optionButtons.Count; i++)
                _optionButtons[i].SetVisible(IsOpen && i < _options.Count);
        }

        void RenderOptionButton(ControlBase control, ControlRenderContext context, List<MySprite> sprites)
        {
            var model = control.DataContext as ComboBoxOptionModel<T>;
            var selected = model != null && EqualityComparer<T>.Default.Equals(_selectedValue, model.Value);
            RenderButton(control.Bounds, model != null ? model.Text : string.Empty, false, selected, context, sprites);
        }

        void RenderButton(RectangleF rect, string text, bool drawArrow, bool selected, ControlRenderContext context,
            List<MySprite> sprites)
        {
            var hovered = rect.Contains(context.CursorPosition);
            var active = hovered || selected;
            var panelColor = context.Style.GetPanelColor(active);
            var textColor = context.Style.GetTextColor(active);
            var textScale = 0.58f * context.Scale * context.FontScale;

            Border.CreateSpritesFromRect(rect, sprites, panelColor, radiusScale: context.Scale);
            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT,
                Data = text ?? string.Empty,
                Position = new Vector2(rect.X + 8f * context.Scale,
                    rect.Center.Y - FormatingHelper.GetSizeInPixel(text ?? string.Empty, "White", textScale, context.Surface).Y * 0.5f),
                RotationOrScale = textScale,
                Color = textColor,
                Alignment = TextAlignment.LEFT,
                FontId = "White"
            });

            if (!drawArrow)
                return;

            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXTURE,
                Data = "Triangle",
                Position = new Vector2(rect.Right - 10f * context.Scale, rect.Center.Y),
                Size = new Vector2(8f * context.Scale, 6f * context.Scale),
                RotationOrScale = IsOpen ? MathHelper.Pi : 0f,
                Color = textColor,
                Alignment = TextAlignment.CENTER
            });
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
