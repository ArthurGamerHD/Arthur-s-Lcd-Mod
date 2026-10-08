using System;
using System.Collections.Generic;
using System.Globalization;
using Adk.Gui.Core.Input;
using LcdMod.Client.Apps.Dialogs;
using LcdMod.Client.Extensions;
using HexColors = LcdMod.Client.Extensions.ColorExtensions;
using LcdMod.Client.Gui.ControlsTemplates.Interactive;
using LcdMod.Common.Mvvm;
using VRageMath;

namespace LcdMod.Client.Apps.ButtonPad
{
    /// <summary>The parameter an action takes, with the current or default value read from a target block.</summary>
    internal sealed class ActionParameterInfo
    {
        public const string BOOLEAN = "Boolean";
        public const string STRING = "String";
        public const string STRING_BUILDER = "StringBuilder";
        public const string INT64 = "Int64";
        public const string SINGLE = "Single";
        public const string COLOR = "Color";
        public const string INCREASE_DECREASE = "IncreaseDecrease";

        public string TypeName;
        public double Minimum = double.MinValue;
        public double Maximum = double.MaxValue;
        public double Number;
        public string Text = string.Empty;
        public Color ColorValue = VRageMath.Color.White;
    }

    /// <summary>Edits one action's parameter: a number, text, color, on/off mode or increase/decrease, plus scrolling.</summary>
    internal sealed class ActionConfigurationViewModel : DialogViewModel
    {
        const string ON = "on";
        const string OFF = "off";
        const string TOGGLE = "toggle";
        const string INCREASE = "increase";
        const string DECREASE = "decrease";
        const string SCROLL_NONE = "none";
        const string SCROLL_NORMAL = "normal";
        const string SCROLL_REVERSED = "reversed";

        readonly ButtonPanelActionSettings _action;
        readonly Action<ButtonPanelActionSettings> _applied;
        readonly string _type;
        readonly double _minimum;
        readonly double _maximum;
        readonly double _step;
        readonly string _format;
        string _number;
        string _text = string.Empty;
        string _color;
        string _booleanMode = TOGGLE;
        string _clickAction = INCREASE;
        string _scrollMode = SCROLL_NONE;

        public ActionConfigurationViewModel(ActionParameterInfo info, ButtonPanelActionSettings action,
            Action<ButtonPanelActionSettings> applied)
        {
            _action = action == null ? new ButtonPanelActionSettings() : action.Clone();
            _applied = applied;
            _type = info == null ? null : info.TypeName;
            ActionName = _action.DisplayName ?? _action.BaseId ?? string.Empty;
            ApplyCommand = new DelegateCommand(_ => Apply());
            IncreaseCommand = new DelegateCommand(_ => Step(1));
            DecreaseCommand = new DelegateCommand(_ => Step(-1));
            BooleanModes = Choices(new[] { ON, OFF, TOGGLE }, mode => _booleanMode = mode, () => _booleanMode, BooleanLabel);
            ClickActions = Choices(new[] { INCREASE, DECREASE }, mode => _clickAction = mode, () => _clickAction, ClickLabel);
            ScrollModes = Choices(new[] { SCROLL_NONE, SCROLL_NORMAL, SCROLL_REVERSED }, mode => _scrollMode = mode, () => _scrollMode, ScrollLabel);

            _minimum = info == null ? 0 : Math.Min(info.Minimum, info.Maximum);
            _maximum = info == null ? 0 : Math.Max(info.Minimum, info.Maximum);
            _format = _type == ActionParameterInfo.INT64 ? "0" : "0.###";
            _step = _type == ActionParameterInfo.INT64 ? 1d : SingleStep(_minimum, _maximum);
            _scrollMode = Normalize(_action.ScrollMode, SCROLL_NONE, SCROLL_NONE, SCROLL_NORMAL, SCROLL_REVERSED);
            _clickAction = Normalize(_action.ClickAction, INCREASE, INCREASE, DECREASE);

            double number = info == null ? 0 : info.Number;
            double parsed;
            if (HasExisting(ActionParameterInfo.INT64) || HasExisting(ActionParameterInfo.SINGLE))
                if (double.TryParse(_action.ParameterValue, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed))
                    number = parsed;
            _number = Clamp(number).ToString(_format, CultureInfo.InvariantCulture);

            if (info != null)
                _text = HasExisting(_type) && (_type == ActionParameterInfo.STRING || _type == ActionParameterInfo.STRING_BUILDER)
                    ? _action.ParameterValue ?? string.Empty
                    : info.Text ?? string.Empty;
            Color color;
            _color = (HasExisting(ActionParameterInfo.COLOR) && HexColors.TryParseHexColor(_action.ParameterValue, out color)
                ? color
                : info == null ? Color.White : info.ColorValue).ToHex();
            if (HasExisting(ActionParameterInfo.BOOLEAN))
                _booleanMode = NormalizeBoolean(_action.ParameterValue);
            RefreshChoices();
        }

        public string ActionName { get; }
        public ICommand ApplyCommand { get; }
        public ICommand IncreaseCommand { get; }
        public ICommand DecreaseCommand { get; }
        public IReadOnlyList<ChoiceButton> BooleanModes { get; }
        public IReadOnlyList<ChoiceButton> ClickActions { get; }
        public IReadOnlyList<ChoiceButton> ScrollModes { get; }
        public bool HasParameter => _type != null;
        public bool HasNoParameter => _type == null;
        public bool IsNumber => _type == ActionParameterInfo.INT64 || _type == ActionParameterInfo.SINGLE;
        public bool IsText => _type == ActionParameterInfo.STRING || _type == ActionParameterInfo.STRING_BUILDER;
        public bool IsColor => _type == ActionParameterInfo.COLOR;
        public bool IsBoolean => _type == ActionParameterInfo.BOOLEAN;
        public bool IsIncreaseDecrease => _type == ActionParameterInfo.INCREASE_DECREASE;
        public bool HasScroll => IsNumber || IsIncreaseDecrease;

        // Compact (pre-migration layout): only the controls, with Apply in a column on the right.
        public bool ShowCaption => HasParameter && !IsCompact;
        public bool ShowScrollLabel => HasScroll && !IsCompact;
        public int ApplyRow => IsCompact ? 0 : 1;
        public int ApplyColumn => IsCompact ? 1 : 0;
        public float Spacing => IsCompact ? 4f : 8f;

        protected override void OnCompactChanged()
        {
            RaisePropertyChanged<bool>(nameof(ShowCaption));
            RaisePropertyChanged<bool>(nameof(ShowScrollLabel));
            RaisePropertyChanged<int>(nameof(ApplyRow));
            RaisePropertyChanged<int>(nameof(ApplyColumn));
            RaisePropertyChanged<float>(nameof(Spacing));
        }

        public string ParameterCaption => Loc(IsNumber ? "LcdMod_ButtonPad_ConfigureAction_NumberValue"
            : IsText ? "LcdMod_ButtonPad_ConfigureAction_TextValue"
            : IsColor ? "LcdMod_ButtonPad_ConfigureAction_ColorValue"
            : IsBoolean ? "LcdMod_ButtonPad_ConfigureAction_BooleanMode"
            : IsIncreaseDecrease ? "LcdMod_ButtonPad_ConfigureAction_ClickAction"
            : "LcdMod_ButtonPad_ConfigureAction_Parameter");

        public string Number
        {
            get { return _number; }
            set { RaiseAndSetIfChanged(ref _number, value ?? string.Empty, nameof(Number)); }
        }

        public string Text
        {
            get { return _text; }
            set { RaiseAndSetIfChanged(ref _text, value ?? string.Empty, nameof(Text)); }
        }

        public string ColorText
        {
            get { return _color; }
            set
            {
                if (!RaiseAndSetIfChanged(ref _color, value ?? string.Empty, nameof(ColorText)))
                    return;
                RaisePropertyChanged<Color>(nameof(ColorPreview));
                RaisePropertyChanged<bool>(nameof(IsColorValid));
            }
        }

        public bool IsColorValid
        {
            get
            {
                Color color;
                return HexColors.TryParseHexColor(_color, out color);
            }
        }

        public Color ColorPreview
        {
            get
            {
                Color color;
                return HexColors.TryParseHexColor(_color, out color) ? color : Color.Transparent;
            }
        }

        void Step(int direction)
        {
            Number = Clamp(ParseNumber() + direction * _step).ToString(_format, CultureInfo.InvariantCulture);
        }

        double ParseNumber()
        {
            double value;
            return double.TryParse(_number, NumberStyles.Float, CultureInfo.InvariantCulture, out value) ? Clamp(value) : Clamp(0);
        }

        double Clamp(double value)
        {
            return _maximum <= _minimum ? value : Math.Max(_minimum, Math.Min(_maximum, value));
        }

        void Apply()
        {
            if (IsColor && !IsColorValid)
                return;
            var result = _action.Clone();
            result.ParameterTypeName = _type;
            result.ClickAction = null;
            result.ScrollMode = null;
            if (_type == ActionParameterInfo.INT64)
            {
                double value = ParseNumber();
                long integer = value <= long.MinValue ? long.MinValue : value >= long.MaxValue ? long.MaxValue : (long)Math.Round(value);
                result.ParameterValue = integer.ToString(CultureInfo.InvariantCulture);
                result.ScrollMode = _scrollMode;
                result.ParameterDisplayValue = WithScroll(result.ParameterValue);
            }
            else if (_type == ActionParameterInfo.SINGLE)
            {
                var value = (float)ParseNumber();
                result.ParameterValue = value.ToString("R", CultureInfo.InvariantCulture);
                result.ScrollMode = _scrollMode;
                result.ParameterDisplayValue = WithScroll(value.ToString(_format, CultureInfo.InvariantCulture));
            }
            else if (IsText)
            {
                result.ParameterValue = _text;
                result.ParameterDisplayValue = _text;
            }
            else if (IsColor)
            {
                Color color;
                HexColors.TryParseHexColor(_color, out color);
                result.ParameterValue = color.ToHex();
                result.ParameterDisplayValue = result.ParameterValue;
            }
            else if (IsBoolean)
            {
                result.ParameterValue = _booleanMode;
                result.ParameterDisplayValue = BooleanLabel(_booleanMode);
            }
            else if (IsIncreaseDecrease)
            {
                result.ClickAction = _clickAction;
                result.ScrollMode = _scrollMode;
                result.ParameterValue = _clickAction;
                result.ParameterDisplayValue = WithScroll(ClickLabel(_clickAction));
            }
            else
            {
                result.ParameterValue = null;
                result.ParameterDisplayValue = null;
            }
            Close();
            if (_applied != null)
                _applied(result);
        }

        string WithScroll(string primary)
        {
            if (_scrollMode == SCROLL_NONE)
                return primary ?? string.Empty;
            return string.Format(CultureInfo.CurrentCulture, Loc("LcdMod_ButtonPad_ConfigureAction_ScrollFormat"),
                primary ?? string.Empty, ScrollLabel(_scrollMode));
        }

        bool HasExisting(string type)
        {
            return string.Equals(_action.ParameterTypeName, type, StringComparison.OrdinalIgnoreCase) && _action.ParameterValue != null;
        }

        IReadOnlyList<ChoiceButton> Choices(string[] values, Action<string> select, Func<string> current, Func<string, string> label)
        {
            var choices = new List<ChoiceButton>();
            foreach (string value in values)
            {
                string captured = value;
                choices.Add(new ChoiceButton(this, label(value), () => current() == captured, () =>
                {
                    select(captured);
                    RefreshChoices();
                }));
            }
            return choices;
        }

        void RefreshChoices()
        {
            if (BooleanModes == null)
                return;
            foreach (var choice in BooleanModes) choice.Refresh();
            foreach (var choice in ClickActions) choice.Refresh();
            foreach (var choice in ScrollModes) choice.Refresh();
        }

        protected override void OnFontSizeChanged()
        {
            foreach (var choice in BooleanModes) choice.RaisePropertyChanged<float>(nameof(ChoiceButton.FontSize));
            foreach (var choice in ClickActions) choice.RaisePropertyChanged<float>(nameof(ChoiceButton.FontSize));
            foreach (var choice in ScrollModes) choice.RaisePropertyChanged<float>(nameof(ChoiceButton.FontSize));
        }

        static string BooleanLabel(string mode)
        {
            return Loc(mode == ON ? "LcdMod_ButtonPad_ConfigureAction_On"
                : mode == OFF ? "LcdMod_ButtonPad_ConfigureAction_Off"
                : "LcdMod_ButtonPad_ConfigureAction_Toggle");
        }

        static string ClickLabel(string mode)
        {
            return Loc(mode == DECREASE ? "LcdMod_ButtonPad_ConfigureAction_Decrease" : "LcdMod_ButtonPad_ConfigureAction_Increase");
        }

        static string ScrollLabel(string mode)
        {
            return Loc(mode == SCROLL_NORMAL ? "LcdMod_ButtonPad_ConfigureAction_Normal"
                : mode == SCROLL_REVERSED ? "LcdMod_ButtonPad_ConfigureAction_Reversed"
                : "LcdMod_ButtonPad_ConfigureAction_None");
        }

        static string NormalizeBoolean(string value)
        {
            if (string.Equals(value, ON, StringComparison.OrdinalIgnoreCase) || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase))
                return ON;
            if (string.Equals(value, OFF, StringComparison.OrdinalIgnoreCase) || string.Equals(value, "false", StringComparison.OrdinalIgnoreCase))
                return OFF;
            return TOGGLE;
        }

        static string Normalize(string value, string fallback, params string[] allowed)
        {
            foreach (string candidate in allowed)
                if (string.Equals(value, candidate, StringComparison.OrdinalIgnoreCase))
                    return candidate;
            return fallback;
        }

        static double SingleStep(double minimum, double maximum)
        {
            bool finite = !double.IsInfinity(minimum) && !double.IsInfinity(maximum) &&
                          Math.Abs(minimum) < 1e12 && Math.Abs(maximum) < 1e12 && maximum > minimum;
            return finite ? Math.Max(0.001d, (maximum - minimum) / 100d) : 1d;
        }
    }

    internal sealed class ChoiceButton : ObservableObject
    {
        readonly DialogViewModel _owner;
        readonly Func<bool> _isSelected;
        string _classes;

        public ChoiceButton(DialogViewModel owner, string label, Func<bool> isSelected, Action choose)
        {
            _owner = owner;
            _isSelected = isSelected;
            Label = label;
            Command = new DelegateCommand(_ => choose());
        }

        public string Label { get; }
        public ICommand Command { get; }
        public string Classes => _classes;
        public float FontSize => _owner.FontSize;

        internal void Refresh()
        {
            RaiseAndSetIfChanged(ref _classes, _isSelected() ? "dialog-choice selected" : "dialog-choice", nameof(Classes));
        }
    }
}
