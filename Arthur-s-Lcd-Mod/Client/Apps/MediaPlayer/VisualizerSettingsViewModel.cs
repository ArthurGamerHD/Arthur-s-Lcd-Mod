using System;
using System.Collections.Generic;
using System.Globalization;
using Acrux.Core.Input;
using LcdMod.Client.Apps.Dialogs;
using LcdMod.Common.Config.Components;
using Adk.Mvvm;
using VRageMath;

namespace LcdMod.Client.Apps.MediaPlayer
{
    /// <summary>Where the visualizer settings window reads and writes the media player's instance settings.</summary>
    internal interface IVisualizerSettingsHost
    {
        MediaVisualizerInstanceSettings[] VisualizerInstances { get; }
        void SetVisualizerInstances(MediaVisualizerInstanceSettings[] instances, bool persist);
        void EditVisualizerXml();
        bool HasUserVisualizerSettings { get; }
        void SaveUserVisualizerSettings();
        void LoadUserVisualizerSettings();
    }

    /// <summary>
    /// The visualizer settings window: one tab per instance, each listing that instance's settings like the desktop
    /// visualizer's configuration window. Edits show on the screen right away and are saved to the block on close.
    /// </summary>
    [PreviewWith("Title", "")]
    [PreviewWith("Tabs", 3)]
    [PreviewWith("SmallFontSize", 0.5525f)]
    [PreviewWith("SelectedTabIndex", 0)]
    [PreviewWith("RemoveText", "Remove")]
    [PreviewWith("ManagedInstances", 2)]
    [PreviewWith("ManageText", "Manage instances")]
    [PreviewWith("IsRegular", true)]
    [PreviewWith("IsManaging", true)]
    [PreviewWith("IsEditing", false)]
    [PreviewWith("IsCompact", false)]
    [PreviewWith("InstanceEnabled", false)]
    [PreviewWith("HasUserSettings", false)]
    [PreviewWith("FontSize", 0.65f)]
    [PreviewWith("AddTypes", 5)]
    internal sealed class VisualizerSettingsViewModel : DialogViewModel
    {
        readonly IVisualizerSettingsHost _host;
        List<VisualizerTabModel> _tabs = new List<VisualizerTabModel>();
        List<VisualizerManagedInstanceModel> _managedInstances = new List<VisualizerManagedInstanceModel>();
        readonly List<VisualizerChoiceModel> _addTypes = new List<VisualizerChoiceModel>();
        readonly ObservableList<VisualizerSettingRow> _rows = new ObservableList<VisualizerSettingRow>();
        MediaVisualizerInstanceSettings[] _instances;
        int _selectedIndex = -1;
        bool _dirty;

        public VisualizerSettingsViewModel(IVisualizerSettingsHost host)
        {
            _host = host;
            ResetCommand = new DelegateCommand(_ => ResetSelected());
            RestoreDefaultsCommand = new DelegateCommand(_ => RestoreDefaults());
            XmlCommand = new DelegateCommand(_ => { Persist(); _host.EditVisualizerXml(); });
            SaveUserCommand = new DelegateCommand(_ => { Persist(); _host.SaveUserVisualizerSettings(); RaiseUserSettings(); });
            LoadUserCommand = new DelegateCommand(_ => { _host.LoadUserVisualizerSettings(); Reload(); });
            for (int i = 0; i < VisualizerInstanceSchema.All.Length; i++)
            {
                var schema = VisualizerInstanceSchema.All[i];
                _addTypes.Add(new VisualizerChoiceModel(schema.Label, new DelegateCommand(_ => Add(schema)))
                    { FontSize = FontSize });
            }
            Reload();
        }

        public IReadOnlyList<VisualizerTabModel> Tabs => _tabs;
        public string ManageText => Loc("LcdMod_MediaPlayer_VisualizerManageInstances");
        public string RemoveText => Loc("LcdMod_MediaPlayer_VisualizerRemoveInstance");
        public IReadOnlyList<VisualizerManagedInstanceModel> ManagedInstances => _managedInstances;
        public IReadOnlyList<VisualizerChoiceModel> AddTypes => _addTypes;
        public IEnumerable<VisualizerSettingRow> Rows => _rows;
        public ICommand ResetCommand { get; }
        public ICommand RestoreDefaultsCommand { get; }
        public ICommand XmlCommand { get; }
        public ICommand SaveUserCommand { get; }
        public ICommand LoadUserCommand { get; }
        public bool HasUserSettings => _host.HasUserVisualizerSettings;
        public bool IsManaging => _selectedIndex < 0;

        // The selected tab: 0 is the manage tab, instance tabs follow in instance order.
        public int SelectedTabIndex
        {
            get { return _selectedIndex + 1; }
            set
            {
                if (value - 1 != _selectedIndex && value >= 0 && value <= _instances.Length)
                    Select(value - 1);
            }
        }
        public bool IsEditing => _selectedIndex >= 0;

        public bool InstanceEnabled
        {
            get { return Current != null && Current.Enabled; }
            set
            {
                if (Current == null || Current.Enabled == value)
                    return;
                Current.Enabled = value;
                Changed();
                RaisePropertyChanged<bool>(nameof(InstanceEnabled));
            }
        }

        MediaVisualizerInstanceSettings Current =>
            _selectedIndex < 0 || _selectedIndex >= _instances.Length ? null : _instances[_selectedIndex];

        VisualizerInstanceSchema SelectedSchema => Current == null ? null : VisualizerInstanceSchema.Get(Current.Type);

        internal void Reload()
        {
            int selectedIndex = _selectedIndex;
            _instances = MediaVisualizerSettingsResolver.Expand(_host.VisualizerInstances);
            _tabs = new List<VisualizerTabModel>
            {
                new VisualizerTabModel(ManageText, this)
            };
            _managedInstances = new List<VisualizerManagedInstanceModel>();
            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < _instances.Length; i++)
            {
                int index = i;
                var schema = VisualizerInstanceSchema.Get(_instances[i].Type);
                string label = schema == null ? _instances[i].Type : schema.Label;
                int count;
                counts.TryGetValue(label, out count);
                counts[label] = ++count;
                if (count > 1) label += " " + count;
                _tabs.Add(new VisualizerTabModel(label, this));
                _managedInstances.Add(new VisualizerManagedInstanceModel(label, RemoveText,
                    new DelegateCommand(_ => Remove(index))) { FontSize = FontSize });
            }
            RaisePropertyChanged<IReadOnlyList<VisualizerTabModel>>(nameof(Tabs));
            RaisePropertyChanged<IReadOnlyList<VisualizerManagedInstanceModel>>(nameof(ManagedInstances));
            Select(_instances.Length == 0 ? -1 : Math.Min(selectedIndex, _instances.Length - 1));
            RaiseUserSettings();
        }

        void Select(int index)
        {
            _selectedIndex = index;
            _rows.Clear();
            var schema = SelectedSchema;
            if (schema != null)
                for (int i = 0; i < schema.Fields.Length; i++)
                    if (schema.Fields[i].Kind != VisualizerFieldKind.HiddenToggle)
                        _rows.Add(new VisualizerSettingRow(schema.Fields[i], FontSize, Get, Set));
            RaisePropertyChanged<bool>(nameof(InstanceEnabled));
            RaisePropertyChanged<bool>(nameof(IsManaging));
            RaisePropertyChanged<bool>(nameof(IsEditing));
            RaisePropertyChanged<int>(nameof(SelectedTabIndex));
        }

        void Add(VisualizerInstanceSchema schema)
        {
            var next = new MediaVisualizerInstanceSettings[_instances.Length + 1];
            Array.Copy(_instances, next, _instances.Length);
            next[next.Length - 1] = MediaVisualizerSettingsResolver.NewDefault(schema);
            _instances = next;
            Changed();
            Reload();
        }

        void Remove(int index)
        {
            if (index < 0 || index >= _instances.Length) return;
            var next = new MediaVisualizerInstanceSettings[_instances.Length - 1];
            if (index > 0) Array.Copy(_instances, 0, next, 0, index);
            if (index < next.Length) Array.Copy(_instances, index + 1, next, index, next.Length - index);
            _instances = next;
            Changed();
            Reload();
        }

        void RestoreDefaults()
        {
            _instances = MediaVisualizerInstanceSettings.Copy(MediaVisualizerDefaults.Instances);
            _selectedIndex = -1;
            Changed();
            Reload();
        }

        float Get(string key)
        {
            return MediaVisualizerSettingsResolver.Get(Current, SelectedSchema, key);
        }

        void Set(string key, float value)
        {
            MediaVisualizerInstanceSettings current = Current;
            if (current == null)
                return;
            for (int i = 0; i < current.Settings.Length; i++)
            {
                if (current.Settings[i].Key != key)
                    continue;
                if (Math.Abs(current.Settings[i].Value - value) < 1e-6f)
                    return;
                current.Settings[i].Value = value;
                Changed();
                if (key == "UseTimeColor" || key == "UseRealTimeColor")
                    foreach (VisualizerSettingRow row in _rows)
                        row.RaiseColorModeChanged();
                return;
            }
        }

        void ResetSelected()
        {
            var current = Current;
            if (current == null) return;
            var schema = SelectedSchema;
            if (schema == null) return;
            _instances[_selectedIndex] = MediaVisualizerSettingsResolver.NewDefault(schema);
            var original = MediaVisualizerSettingsResolver.Find(MediaVisualizerDefaults.Instances, schema.Type);
            if (original != null) _instances[_selectedIndex].Enabled = original.Enabled;
            _instances = MediaVisualizerSettingsResolver.Expand(_instances);
            Changed();
            Select(_selectedIndex);
        }

        void Changed()
        {
            _dirty = true;
            _host.SetVisualizerInstances(MediaVisualizerInstanceSettings.Copy(_instances), false);
        }

        void Persist()
        {
            if (!_dirty)
                return;
            _dirty = false;
            _host.SetVisualizerInstances(MediaVisualizerInstanceSettings.Copy(_host.VisualizerInstances), true);
        }

        void RaiseUserSettings()
        {
            RaisePropertyChanged<bool>(nameof(HasUserSettings));
        }

        protected override void OnFontSizeChanged()
        {
            foreach (VisualizerChoiceModel type in _addTypes)
                type.FontSize = FontSize;
            foreach (VisualizerManagedInstanceModel instance in _managedInstances)
                instance.FontSize = FontSize;
            foreach (VisualizerSettingRow row in _rows)
                row.FontSize = FontSize;
        }

        protected internal override void OnClosed()
        {
            Persist();
        }
    }

    internal sealed class VisualizerManagedInstanceModel : ObservableObject
    {
        float _fontSize;

        public VisualizerManagedInstanceModel(string label, string removeText, ICommand removeCommand)
        {
            Label = label;
            RemoveText = removeText;
            RemoveCommand = removeCommand;
        }

        [PreviewWith("Shader", "Circle")]
        public string Label { get; }
        [PreviewWith("Remove")]
        public string RemoveText { get; }
        public ICommand RemoveCommand { get; }
        [PreviewWith(0.65f)]
        public float FontSize
        {
            get { return _fontSize; }
            set { RaiseAndSetIfChanged(ref _fontSize, value, nameof(FontSize)); }
        }
    }

    /// <summary>A tab of the settings window; every tab shows the window's page for the current selection.</summary>
    internal sealed class VisualizerTabModel : ObservableObject
    {
        public VisualizerTabModel(string label, VisualizerSettingsViewModel owner)
        {
            Label = label;
            Owner = owner;
        }

        [PreviewWith("Manage instances", "Shader", "Circle")]
        public string Label { get; }
        public VisualizerSettingsViewModel Owner { get; }

        public override string ToString()
        {
            return Label;
        }
    }

    internal sealed class VisualizerChoiceModel : ObservableObject
    {
        float _fontSize;

        public VisualizerChoiceModel(string label, ICommand command)
        {
            Label = label;
            Command = command;
        }

        [PreviewWith("Spectrum", "Waveform", "Triangle", "Circle", "Shader")]
        public string Label { get; }
        public ICommand Command { get; }

        [PreviewWith(0.65f)]
        public float FontSize
        {
            get { return _fontSize; }
            set { RaiseAndSetIfChanged(ref _fontSize, value, nameof(FontSize)); }
        }
    }

    internal enum VisualizerColorMode { Fixed, Rgb, TimeBased }

    /// <summary>One line of the settings window: a section header, a slider, a checkbox or a color.</summary>
    internal sealed class VisualizerSettingRow : ObservableObject
    {
        readonly VisualizerField _field;
        readonly Func<string, float> _get;
        readonly Action<string, float> _set;
        float _fontSize;

        public VisualizerSettingRow(VisualizerField field, float fontSize, Func<string, float> get,
            Action<string, float> set)
        {
            _field = field;
            _fontSize = fontSize;
            _get = get;
            _set = set;
        }

        public string Label => _field.Label;
        public bool IsHeader => _field.Kind == VisualizerFieldKind.Header;
        public bool IsToggle => _field.Kind == VisualizerFieldKind.Toggle;
        public bool IsColor => _field.Kind == VisualizerFieldKind.Color;
        public bool IsColorMode => _field.Kind == VisualizerFieldKind.ColorMode;
        public bool IsSlider => !IsHeader && !IsToggle && !IsColor && !IsColorMode;
        public float Minimum => _field.Minimum;
        public float Maximum => _field.Maximum;
        public float Step => _field.Kind == VisualizerFieldKind.Integer ? 1f : 0f;
        public bool SnapToStep => _field.Kind == VisualizerFieldKind.Integer;

        public float FontSize
        {
            get { return _fontSize; }
            set { RaiseAndSetIfChanged(ref _fontSize, value, nameof(FontSize)); }
        }

        public float Value
        {
            get { return IsSlider ? _get(_field.Key) : 0f; }
            set
            {
                if (!IsSlider)
                    return;
                if (_field.Kind == VisualizerFieldKind.Integer)
                    value = (float)Math.Round(value);
                _set(_field.Key, value);
                RaisePropertyChanged<float>(nameof(Value));
                RaisePropertyChanged<string>(nameof(ValueText));
            }
        }

        public string ValueText
        {
            get
            {
                if (!IsSlider)
                    return string.Empty;
                float value = Value;
                return _field.Kind == VisualizerFieldKind.Integer
                    ? ((int)value).ToString(CultureInfo.InvariantCulture)
                    : value.ToString("0.###", CultureInfo.InvariantCulture);
            }
        }

        public bool IsChecked
        {
            get { return IsToggle && _get(_field.Key) >= .5f; }
            set
            {
                if (!IsToggle)
                    return;
                _set(_field.Key, value ? 1f : 0f);
                RaisePropertyChanged<bool>(nameof(IsChecked));
            }
        }

        public VisualizerColorMode ColorMode
        {
            get
            {
                // The renderer gives rainbow colors precedence for legacy files with both flags set.
                if (_get("UseTimeColor") >= .5f) return VisualizerColorMode.Rgb;
                if (_get("UseRealTimeColor") >= .5f) return VisualizerColorMode.TimeBased;
                return VisualizerColorMode.Fixed;
            }
            set
            {
                if (!IsColorMode) return;
                _set("UseTimeColor", value == VisualizerColorMode.Rgb ? 1f : 0f);
                _set("UseRealTimeColor", value == VisualizerColorMode.TimeBased ? 1f : 0f);
                RaisePropertyChanged<VisualizerColorMode>(nameof(ColorMode));
            }
        }

        internal void RaiseColorModeChanged()
        {
            RaisePropertyChanged<VisualizerColorMode>(nameof(ColorMode));
        }

        public float Red
        {
            get { return Channel("ColorR"); }
            set { SetChannel("ColorR", value, nameof(Red)); }
        }

        public float Green
        {
            get { return Channel("ColorG"); }
            set { SetChannel("ColorG", value, nameof(Green)); }
        }

        public float Blue
        {
            get { return Channel("ColorB"); }
            set { SetChannel("ColorB", value, nameof(Blue)); }
        }

        public Color Swatch => IsColor ? new Color(Red, Green, Blue) : Color.Transparent;

        float Channel(string key)
        {
            return IsColor ? _get(key) : 0f;
        }

        void SetChannel(string key, float value, string property)
        {
            if (!IsColor)
                return;
            _set(key, value);
            RaisePropertyChanged<float>(property);
            RaisePropertyChanged<Color>(nameof(Swatch));
        }
    }
}
