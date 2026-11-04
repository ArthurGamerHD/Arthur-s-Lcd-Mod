using Adk.Mvvm;
using System;
using System.Globalization;
using Acrux.Core.Input;
using LcdMod.Client.Apps.Dialogs;
using LcdMod.Client.Extensions;
using HexColors = LcdMod.Client.Extensions.ColorExtensions;
using LcdMod.Client.Gui.ControlsTemplates.Interactive;
using VRageMath;

namespace LcdMod.Client.Apps.ButtonPad
{
    /// <summary>The pickers the entry editor opens on top of itself.</summary>
    internal sealed class ButtonPadEntryServices
    {
        public Action<Action<string>> PickSprite;
        public Action<PickActionTargetResult, Action<PickActionTargetResult>> PickTarget;
        public Action<ButtonPanelTargetSettings, ButtonPanelActionSettings, Action<ButtonPanelActionSettings>> PickAction;
        public Func<ButtonPanelTargetSettings, ButtonPanelActionSettings, bool> NeedsConfiguration;
        public Action<ButtonPanelTargetSettings, ButtonPanelActionSettings, Action<ButtonPanelActionSettings>> Configure;
    }

    /// <summary>Edits a draft of one button pad entry; Apply hands back the draft and Delete hands back null.</summary>
    [PreviewWith("Title", "Button 3")]
    [PreviewWith("SmallFontSize", 0.5525f)]
    [PreviewWith("IsRegular", true)]
    [PreviewWith("IsCompact", false)]
    [PreviewWith("FontSize", 0.65f)]
    internal sealed class ButtonPadEntryViewModel : DialogViewModel
    {
        readonly ButtonPanelEntrySettings _draft;
        readonly Action<ButtonPanelEntrySettings> _apply;
        readonly ButtonPadEntryServices _services;
        string _color;

        public ButtonPadEntryViewModel(int index, ButtonPanelEntrySettings initial, Action<ButtonPanelEntrySettings> apply,
            ButtonPadEntryServices services)
        {
            _draft = initial == null ? new ButtonPanelEntrySettings() : initial.Clone();
            _draft.Index = index;
            _apply = apply;
            _services = services;
            _color = _draft.BackgroundColor ?? string.Empty;
            Title = index >= 0
                ? string.Format(CultureInfo.CurrentCulture, Loc("LcdMod_ButtonPad_ButtonFormat"), index + 1)
                : Loc("LcdMod_ButtonPad_Button");
            PickSpriteCommand = new DelegateCommand(_ => _services.PickSprite(sprite =>
            {
                _draft.SpriteName = sprite;
                RaiseSprite();
            }));
            ClearSpriteCommand = new DelegateCommand(_ =>
            {
                _draft.SpriteName = null;
                RaiseSprite();
            });
            ClearColorCommand = new DelegateCommand(_ => ColorText = string.Empty);
            PickTargetCommand = new DelegateCommand(_ => _services.PickTarget(_draft.Target == null ? null : _draft.Target.ToPickResult(), OnTargetPicked));
            ClearTargetCommand = new DelegateCommand(_ =>
            {
                _draft.Target = null;
                _draft.Action = null;
                RaiseTarget();
            });
            PickActionCommand = new DelegateCommand(_ =>
            {
                if (_draft.Target != null)
                    _services.PickAction(_draft.Target, _draft.Action, OnActionPicked);
            });
            ClearActionCommand = new DelegateCommand(_ =>
            {
                _draft.Action = null;
                RaiseTarget();
            });
            ApplyCommand = new DelegateCommand(_ => Finish(_draft.Clone()));
            DeleteCommand = new DelegateCommand(_ => Finish(null));
        }

        public ICommand PickSpriteCommand { get; }
        public ICommand ClearSpriteCommand { get; }
        public ICommand ClearColorCommand { get; }
        public ICommand PickTargetCommand { get; }
        public ICommand ClearTargetCommand { get; }
        public ICommand PickActionCommand { get; }
        public ICommand ClearActionCommand { get; }
        public ICommand ApplyCommand { get; }
        public ICommand DeleteCommand { get; }
        public ButtonPanelEntrySettings Draft => _draft;

        [PreviewWith("Hangar")]
        public string EntryTitle
        {
            get { return _draft.Title ?? string.Empty; }
            set
            {
                if (string.Equals(_draft.Title ?? string.Empty, value ?? string.Empty, StringComparison.Ordinal))
                    return;
                _draft.Title = value ?? string.Empty;
                RaisePropertyChanged<string>(nameof(EntryTitle));
            }
        }

        public string ColorText
        {
            get { return _color; }
            set
            {
                if (!RaiseAndSetIfChanged(ref _color, value ?? string.Empty, nameof(ColorText)))
                    return;
                Color color;
                if (_color.Trim().Length == 0)
                    _draft.BackgroundColor = null;
                else if (HexColors.TryParseHexColor(_color, out color))
                    _draft.BackgroundColor = color.ToHex();
                RaisePropertyChanged<bool>(nameof(IsColorInvalid));
                RaisePropertyChanged<string>(nameof(ColorInputClasses));
                RaisePropertyChanged<bool>(nameof(HasColor));
                RaisePropertyChanged<Color>(nameof(PreviewColor));
            }
        }

        public bool IsColorInvalid
        {
            get
            {
                Color color;
                return _color.Trim().Length > 0 && !HexColors.TryParseHexColor(_color, out color);
            }
        }

        public string ColorInputClasses => IsColorInvalid ? "invalid" : string.Empty;
        public bool HasColor => !string.IsNullOrEmpty(_draft.BackgroundColor);

        public Color PreviewColor
        {
            get
            {
                Color color;
                return HexColors.TryParseHexColor(_draft.BackgroundColor, out color) ? color : Color.Transparent;
            }
        }

        public bool HasSprite => !string.IsNullOrEmpty(_draft.SpriteName);
        [PreviewWith("Plus")]
        public string PreviewIcon => HasSprite ? _draft.SpriteName : "Plus";

        [PreviewWith("Hangar Doors")]
        public string TargetText => _draft.Target == null || string.IsNullOrEmpty(_draft.Target.DisplayName)
            ? Loc("LcdMod_ButtonPad_SelectTarget")
            : _draft.Target.DisplayName;

        public string TargetIcon => _draft.Target == null ? null : _draft.Target.SpriteName;
        public bool HasTargetIcon => !string.IsNullOrEmpty(TargetIcon);

        [PreviewWith(true)]
        public bool HasTarget => _draft.Target != null;
        [PreviewWith(true)]
        public bool HasAction => _draft.Action != null;

        [PreviewWith("Toggle block On/Off")]
        public string ActionText
        {
            get
            {
                var action = _draft.Action;
                if (_draft.Target == null || action == null || string.IsNullOrEmpty(action.DisplayName))
                    return Loc("LcdMod_ButtonPad_SelectAction");
                return string.IsNullOrEmpty(action.ParameterDisplayValue)
                    ? action.DisplayName
                    : action.DisplayName + " = " + action.ParameterDisplayValue;
            }
        }

        [PreviewWith("BooleanInput")]
        public string ActionIcon => _draft.Target == null || _draft.Action == null ? null : _draft.Action.SpriteName;
        [PreviewWith(true)]
        public bool HasActionIcon => !string.IsNullOrEmpty(ActionIcon);

        void OnTargetPicked(PickActionTargetResult target)
        {
            string previous = _draft.Target == null ? null : _draft.Target.CompatibilityKey;
            _draft.Target = ButtonPanelTargetSettings.FromPickResult(target);
            string next = _draft.Target == null ? null : _draft.Target.CompatibilityKey;
            if (!string.Equals(previous, next, StringComparison.OrdinalIgnoreCase))
                _draft.Action = null;
            RaiseTarget();
        }

        void OnActionPicked(ButtonPanelActionSettings action)
        {
            var selected = action == null ? null : action.Clone();
            if (selected != null && _draft.Action != null &&
                string.Equals(selected.BaseId, _draft.Action.BaseId, StringComparison.OrdinalIgnoreCase))
                selected.CopyParametersFrom(_draft.Action);
            if (selected != null && _services.NeedsConfiguration(_draft.Target, selected))
            {
                _services.Configure(_draft.Target, selected, OnActionConfigured);
                return;
            }
            _draft.Action = selected;
            RaiseTarget();
        }

        void OnActionConfigured(ButtonPanelActionSettings action)
        {
            _draft.Action = action == null ? null : action.Clone();
            RaiseTarget();
        }

        void Finish(ButtonPanelEntrySettings result)
        {
            Close();
            if (_apply != null)
                _apply(result);
        }

        void RaiseSprite()
        {
            RaisePropertyChanged<bool>(nameof(HasSprite));
            RaisePropertyChanged<string>(nameof(PreviewIcon));
        }

        void RaiseTarget()
        {
            RaisePropertyChanged<string>(nameof(TargetText));
            RaisePropertyChanged<string>(nameof(TargetIcon));
            RaisePropertyChanged<bool>(nameof(HasTargetIcon));
            RaisePropertyChanged<string>(nameof(ActionText));
            RaisePropertyChanged<string>(nameof(ActionIcon));
            RaisePropertyChanged<bool>(nameof(HasActionIcon));
            RaisePropertyChanged<bool>(nameof(HasTarget));
            RaisePropertyChanged<bool>(nameof(HasAction));
        }
    }
}
