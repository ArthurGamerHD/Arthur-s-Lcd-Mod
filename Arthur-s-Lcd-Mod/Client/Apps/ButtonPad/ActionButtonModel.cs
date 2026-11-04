using Acrux.Core.Input;
using System;
using Adk.Mvvm;

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
        float _size = 92f;
        ICommand _command;
        ICommand _secondaryCommand;

        public ICommand Command => _command ?? (_command = new DelegateCommand(_ => { var action = Clicked; if (action != null) action(); }));
        public ICommand SecondaryCommand => _secondaryCommand ?? (_secondaryCommand = new DelegateCommand(_ => { var action = SecondaryClicked; if (action != null) action(); }));
        [PreviewWith("Hangar", "Lights", "Drill", "Crane", "Alarm", "")]
        public string Title { get { return _title; } set { RaiseAndSetIfChanged(ref _title, value, nameof(Title)); } }
        [PreviewWith("MyObjectBuilder_AirtightSlideDoor/LargeBlockSlideDoor", "MyObjectBuilder_InteriorLight/SmallLight", "MyObjectBuilder_Drill/LargeBlockDrill", "MyObjectBuilder_MotorStator/LargeStator", "Danger", "Plus")]
        public string Icon { get { return _icon; } set { RaiseAndSetIfChanged(ref _icon, value, nameof(Icon)); } }
        [PreviewWith("Open", "On", "Off", "0.5 rpm", "", "")]
        public string Status { get { return _status; } set { RaiseAndSetIfChanged(ref _status, value, nameof(Status)); } }
        [PreviewWith("default")]
        public string Variant { get { return _variant; } set { RaiseAndSetIfChanged(ref _variant, value, nameof(Variant)); } }
        [PreviewWith(true, true, true, true, true, false)]
        public bool IsConfigured { get { return _isConfigured; } set { RaiseAndSetIfChanged(ref _isConfigured, value, nameof(IsConfigured)); } }
        // The slot's configured color, transparent when it uses the theme's. The square button's side; it is centered
        // in a larger layout cell so all gaps are even.
        [PreviewWith(140f)]
        public float Size { get { return _size; } set { RaiseAndSetIfChanged(ref _size, value, nameof(Size)); } }
        [PreviewWith("#00000000", "#00000000", "#8C3C28", "#00000000", "#A01E1E", "#00000000")]
        public VRageMath.Color PanelColor { get { return _panelColor; } set { RaiseAndSetIfChanged(ref _panelColor, value, nameof(PanelColor)); } }
        public Action Clicked { get; set; }
        public Action SecondaryClicked { get; set; }
    }
}
