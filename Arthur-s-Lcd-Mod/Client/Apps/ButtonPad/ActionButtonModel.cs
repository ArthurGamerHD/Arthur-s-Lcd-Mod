using Adk.Gui.Core.Input;
using System;
using LcdMod.Common.Mvvm;

namespace LcdMod.Client.Apps.ButtonPad
{
    internal sealed class ActionButtonModel : ObservableObject
    {
        string _title = string.Empty;
        string _icon = "MissingIcon";
        string _status = string.Empty;
        string _variant = string.Empty;
        bool _isConfigured = true;
        VRageMath.Color _panelColor = VRageMath.Color.Transparent;
        ICommand _command;
        ICommand _secondaryCommand;

        public ICommand Command => _command ?? (_command = new DelegateCommand(_ => { var action = Clicked; if (action != null) action(); }));
        public ICommand SecondaryCommand => _secondaryCommand ?? (_secondaryCommand = new DelegateCommand(_ => { var action = SecondaryClicked; if (action != null) action(); }));
        public string Title { get { return _title; } set { RaiseAndSetIfChanged(ref _title, value, nameof(Title)); } }
        public string Icon { get { return _icon; } set { RaiseAndSetIfChanged(ref _icon, value, nameof(Icon)); } }
        public string Status { get { return _status; } set { RaiseAndSetIfChanged(ref _status, value, nameof(Status)); } }
        public string Variant { get { return _variant; } set { RaiseAndSetIfChanged(ref _variant, value, nameof(Variant)); } }
        public bool IsConfigured { get { return _isConfigured; } set { RaiseAndSetIfChanged(ref _isConfigured, value, nameof(IsConfigured)); } }
        /// <summary>The slot's configured color, transparent when it uses the theme's.</summary>
        public VRageMath.Color PanelColor { get { return _panelColor; } set { RaiseAndSetIfChanged(ref _panelColor, value, nameof(PanelColor)); } }
        public Action Clicked { get; set; }
        public Action SecondaryClicked { get; set; }
    }
}
