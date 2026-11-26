using System;
using System.Collections.Generic;
using System.Text;
using Acrux.Core.Input;
using Adk.Mvvm;
using LcdMod.Client.Config;
using LcdMod.Client.Terminal.Controls;
using LcdMod.Client.Terminal.Controls.Filter.Listbox;
using LcdMod.Common.Config.Components;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage;
using VRage.ModAPI;
using VRage.Utils;
using VRageMath;

namespace LcdMod.Client.Apps.MultiFunction
{
    /// <summary>
    /// One of the terminal controls a hosted app's own surface would show, run against the hosted app's config: the
    /// same getters and setters as in the terminal.
    /// </summary>
    internal sealed class TerminalSettingRow : ObservableObject
    {
        readonly TerminalControlsWrapper _wrapper;
        readonly IMyTerminalBlock _block;
        readonly IAppConfig _config;
        readonly string _script;
        readonly Action _changed;
        readonly IMyTerminalControl _control;
        readonly List<MyTerminalControlComboBoxItem> _comboItems = new List<MyTerminalControlComboBoxItem>();
        readonly List<MyTerminalControlListBoxItem> _listItems = new List<MyTerminalControlListBoxItem>();
        readonly List<MyTerminalControlListBoxItem> _listSelected = new List<MyTerminalControlListBoxItem>();
        readonly StringBuilder _writer = new StringBuilder();
        IReadOnlyList<string> _options = new string[0];
        IReadOnlyList<TerminalListEntry> _entries = new TerminalListEntry[0];
        bool _isOn;
        float _value;
        bool _valuePending;
        long _pendingSince;
        string _valueInput = string.Empty;
        int _selectedOption = -1;
        Color _color;
        string _valueText = string.Empty;
        bool _isEnabled = true;

        public TerminalSettingRow(TerminalControlsWrapper wrapper, IMyTerminalBlock block, IAppConfig config, string script,
            Action changed)
        {
            _script = script;
            _wrapper = wrapper;
            _block = block;
            _config = config;
            _changed = changed;
            _control = wrapper.TerminalControl;
            var titled = _control as IMyTerminalControlTitleTooltip;
            Title = titled == null ? string.Empty : MyTexts.GetString(titled.Title);
            var label = _control as IMyTerminalControlLabel;
            if (label != null)
                Title = MyTexts.GetString(label.Label);
            PressCommand = new DelegateCommand(_ => Run(() =>
            {
                var button = _control as IMyTerminalControlButton;
                if (button != null && button.Action != null)
                    button.Action(_block);
            }, true));
            Refresh();
        }

        internal TerminalControlsWrapper Wrapper => _wrapper;
        public string Title { get; }
        public bool IsSwitch => _control is IMyTerminalControlOnOffSwitch || _control is IMyTerminalControlCheckbox;
        public bool IsSlider => _control is IMyTerminalControlSlider;
        public bool IsCombo => _control is IMyTerminalControlCombobox;
        public bool IsColor => _control is IMyTerminalControlColor;
        public bool IsButton => _control is IMyTerminalControlButton;
        public bool IsList => _wrapper is TerminalControlsListbox;
        public bool IsHeader => _control is IMyTerminalControlLabel || _control is IMyTerminalControlSeparator;
        public bool HasTitle => !string.IsNullOrEmpty(Title);
        public float Minimum => _wrapper.SliderMinimum;
        public float Maximum => _wrapper.SliderMaximum;
        public ICommand PressCommand { get; }

        /// <summary>Whether a terminal shows this kind of control as an editable row here.</summary>
        public static bool IsSupported(TerminalControlsWrapper wrapper)
        {
            var control = wrapper.TerminalControl;
            return wrapper is TerminalControlsListbox || control is IMyTerminalControlOnOffSwitch || control is IMyTerminalControlCheckbox ||
                   control is IMyTerminalControlSlider || control is IMyTerminalControlCombobox ||
                   control is IMyTerminalControlColor || control is IMyTerminalControlButton ||
                   control is IMyTerminalControlLabel;
        }

        public bool IsEnabled
        {
            get { return _isEnabled; }
            private set { RaiseAndSetIfChanged(ref _isEnabled, value, nameof(IsEnabled)); }
        }

        public bool IsOn
        {
            get { return _isOn; }
            set
            {
                if (_isOn == value)
                    return;
                Run(() =>
                {
                    var onOff = _control as IMyTerminalControlOnOffSwitch;
                    if (onOff != null && onOff.Setter != null)
                        onOff.Setter(_block, value);
                    var check = _control as IMyTerminalControlCheckbox;
                    if (check != null && check.Setter != null)
                        check.Setter(_block, value);
                }, true);
            }
        }

        // A drag moves the slider every frame; the terminal setter (which saves and syncs the config) and the app's new
        // layout run from FlushValue at a lower rate, and the dragged value is kept until then.
        public float Value
        {
            get { return _value; }
            set
            {
                if (Math.Abs(_value - value) < 1e-5f)
                    return;
                if (!_valuePending)
                    _pendingSince = Sandbox.ModAPI.MyAPIGateway.Session == null ? 0 : Sandbox.ModAPI.MyAPIGateway.Session.GameplayFrameCounter;
                _value = value;
                _valuePending = true;
                RaisePropertyChanged<float>(nameof(Value));
                SetValueInput(FormatValue(value));
            }
        }

        internal bool HasPendingValue => _valuePending;
        internal long PendingSince => _pendingSince;

        internal void FlushValue()
        {
            if (!_valuePending)
                return;
            _valuePending = false;
            float value = _value;
            Run(() =>
            {
                var slider = _control as IMyTerminalControlSlider;
                if (slider != null && slider.Setter != null)
                    slider.Setter(_block, value);
            }, true);
        }

        public string ValueText => _valueText;

        /// <summary>The slider's value as typed in its text box; a valid number is applied right away.</summary>
        public string ValueInput
        {
            get { return _valueInput; }
            set
            {
                string text = value ?? string.Empty;
                if (text == _valueInput)
                    return;
                _valueInput = text;
                RaisePropertyChanged<string>(nameof(ValueInput));
                float parsed;
                if (!float.TryParse(text.Trim().Replace(',', '.'), System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out parsed))
                    return;
                parsed = MathHelper.Clamp(parsed, Minimum, Maximum);
                if (Math.Abs(parsed - _value) < 1e-5f && !_valuePending)
                    return;
                _value = parsed;
                _valuePending = true;
                RaisePropertyChanged<float>(nameof(Value));
                FlushValue();
            }
        }

        static string FormatValue(float value)
        {
            return value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        }

        // Keeps what is being typed while it still means the current value (e.g. "1." for 1).
        void SetValueInput(string formatted)
        {
            float typed;
            float current;
            if (float.TryParse(_valueInput.Trim().Replace(',', '.'), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out typed) &&
                float.TryParse(formatted, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out current) &&
                Math.Abs(typed - current) < 1e-4f)
                return;
            RaiseAndSetIfChanged(ref _valueInput, formatted, nameof(ValueInput));
        }

        public IReadOnlyList<string> Options => _options;

        public int SelectedOption
        {
            get { return _selectedOption; }
            set
            {
                if (value == _selectedOption || value < 0 || value >= _comboItems.Count)
                    return;
                long key = _comboItems[value].Key;
                Run(() =>
                {
                    var combo = _control as IMyTerminalControlCombobox;
                    if (combo != null && combo.Setter != null)
                        combo.Setter(_block, key);
                }, true);
            }
        }

        public float Red
        {
            get { return _color.R; }
            set { SetColor(new Color((int)value, _color.G, _color.B, _color.A)); }
        }

        public float Green
        {
            get { return _color.G; }
            set { SetColor(new Color(_color.R, (int)value, _color.B, _color.A)); }
        }

        public float Blue
        {
            get { return _color.B; }
            set { SetColor(new Color(_color.R, _color.G, (int)value, _color.A)); }
        }

        public Color Swatch => new Color(_color.R, _color.G, _color.B);

        public IReadOnlyList<TerminalListEntry> Entries => _entries;

        void SetColor(Color color)
        {
            if (color == _color)
                return;
            Run(() =>
            {
                var picker = _control as IMyTerminalControlColor;
                if (picker != null && picker.Setter != null)
                    picker.Setter(_block, color);
            }, true);
        }

        internal void SelectEntry(TerminalListEntry entry)
        {
            Run(() =>
            {
                var list = _wrapper as TerminalControlsListbox;
                if (list == null)
                    return;
                var selection = new List<MyTerminalControlListBoxItem>();
                if (list.Multiselect)
                    foreach (var current in _entries)
                        if (ReferenceEquals(current, entry) ? !current.IsSelected : current.IsSelected)
                            selection.Add(current.Item);
                if (!list.Multiselect)
                    selection.Add(entry.Item);
                list.Select(_block, selection);
            }, true);
        }

        /// <summary>Reads the control's current value again, as the terminal does when it redraws.</summary>
        public void Refresh()
        {
            Run(ReadValues, false);
        }

        void ReadValues()
        {
            IsEnabled = _control.Enabled == null || _control.Enabled(_block);
            var onOff = _control as IMyTerminalControlOnOffSwitch;
            var check = _control as IMyTerminalControlCheckbox;
            bool on = onOff != null && onOff.Getter != null ? onOff.Getter(_block)
                : check != null && check.Getter != null && check.Getter(_block);
            RaiseAndSetIfChanged(ref _isOn, on, nameof(IsOn));

            var slider = _control as IMyTerminalControlSlider;
            if (slider != null && !_valuePending)
            {
                float value = slider.Getter == null ? 0f : slider.Getter(_block);
                RaiseAndSetIfChanged(ref _value, value, nameof(Value));
                SetValueInput(FormatValue(value));
                _writer.Clear();
                if (slider.Writer != null)
                    slider.Writer(_block, _writer);
                SetValueText(_writer.ToString());
            }

            var combo = _control as IMyTerminalControlCombobox;
            if (combo != null)
            {
                if (_comboItems.Count == 0 && combo.ComboBoxContent != null)
                {
                    combo.ComboBoxContent(_comboItems);
                    var options = new string[_comboItems.Count];
                    for (int i = 0; i < options.Length; i++)
                        options[i] = MyTexts.GetString(_comboItems[i].Value);
                    _options = options;
                    RaisePropertyChanged<IReadOnlyList<string>>(nameof(Options));
                }
                long key = combo.Getter == null ? 0 : combo.Getter(_block);
                int index = -1;
                for (int i = 0; i < _comboItems.Count; i++)
                    if (_comboItems[i].Key == key)
                        index = i;
                RaiseAndSetIfChanged(ref _selectedOption, index, nameof(SelectedOption));
            }

            var picker = _control as IMyTerminalControlColor;
            if (picker != null)
            {
                Color color = picker.Getter == null ? Color.White : picker.Getter(_block);
                if (color != _color)
                {
                    _color = color;
                    RaisePropertyChanged<float>(nameof(Red));
                    RaisePropertyChanged<float>(nameof(Green));
                    RaisePropertyChanged<float>(nameof(Blue));
                    RaisePropertyChanged<Color>(nameof(Swatch));
                }
                SetValueText(color.R + ", " + color.G + ", " + color.B);
            }

            var list = _wrapper as TerminalControlsListbox;
            if (list != null)
            {
                _listItems.Clear();
                _listSelected.Clear();
                list.ReadItems(_block, _listItems, _listSelected);
                var entries = new List<TerminalListEntry>(_listItems.Count);
                foreach (var item in _listItems)
                    entries.Add(new TerminalListEntry(this, item, _listSelected.Contains(item)));
                _entries = entries;
                RaisePropertyChanged<IReadOnlyList<TerminalListEntry>>(nameof(Entries));
            }
        }

        void SetValueText(string text)
        {
            RaiseAndSetIfChanged(ref _valueText, text ?? string.Empty, nameof(ValueText));
        }

        void Run(Action action, bool notify)
        {
            ConfigManager.RunNested(_block, _config, _script, action);
            if (notify && _changed != null)
                _changed();
        }
    }

    /// <summary>One item of a terminal list box row.</summary>
    internal sealed class TerminalListEntry : ObservableObject
    {
        readonly TerminalSettingRow _owner;

        public TerminalListEntry(TerminalSettingRow owner, MyTerminalControlListBoxItem item, bool selected)
        {
            _owner = owner;
            Item = item;
            IsSelected = selected;
            Text = MyTexts.GetString(item.Text);
            ToggleCommand = new DelegateCommand(_ => _owner.SelectEntry(this));
        }

        internal MyTerminalControlListBoxItem Item { get; }
        public string Text { get; }
        public bool IsSelected { get; }
        public string Classes => IsSelected ? "mfd-list-entry selected" : "mfd-list-entry";
        public ICommand ToggleCommand { get; }
    }
}
