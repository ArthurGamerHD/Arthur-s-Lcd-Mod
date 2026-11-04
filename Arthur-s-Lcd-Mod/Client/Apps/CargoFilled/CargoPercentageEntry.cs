using Acrux.Core.Input;
using System;
using Adk.Mvvm;
using VRageMath;

namespace LcdMod.Client.Apps.CargoFilled
{
    internal sealed class CargoPercentageEntry : ObservableObject
    {
        ICommand _command;
        public ICommand Command => _command ?? (_command = new DelegateCommand(_ => { var action = Clicked; if (action != null) action(); }));
        string _icon;
        string _name;
        float _percentage;
        string _percentageText;
        Action _clicked;
        string _classes = "card";
        Color _fill;

        public CargoPercentageEntry(long key)
        {
            Key = key;
        }

        [PreviewWith(1L, 2L, 3L, 4L)]
        public long Key { get; private set; }
        [PreviewWith("MyObjectBuilder_CargoContainer/LargeBlockLargeContainer", "MyObjectBuilder_CargoContainer/LargeBlockLargeContainer", "MyObjectBuilder_CargoContainer/LargeBlockSmallContainer", "MyObjectBuilder_ShipConnector/Connector")]
        public string Icon => _icon;
        [PreviewWith("Large Cargo Container 1", "Large Cargo Container 2", "Ore Buffer", "Connector (Dock A)")]
        public string Name => _name;
        [PreviewWith(0.8059259f, 0.28444445f, 0.9728f, 0f)]
        public float Percentage => _percentage;
        [PreviewWith("81%", "28%", "97%", "0%")]
        public string PercentageText => _percentageText;
        public Action Clicked => _clicked;
        [PreviewWith("card")]
        public string Classes => _classes;
        [PreviewWith("#E0A010", "#D1B1D7", "#602020", "#D1B1D7")]
        public Color Fill => _fill;

        public void UpdateAppearance(string classes, Color fill)
        {
            Set(ref _classes, classes, nameof(Classes));
            Set(ref _fill, fill, nameof(Fill));
        }

        public void Update(string icon, string name, float percentage, string percentageText, Action clicked)
        {
            Set(ref _icon, string.IsNullOrWhiteSpace(icon) ? "MissingIcon" : icon, nameof(Icon));
            Set(ref _name, name ?? string.Empty, nameof(Name));
            Set(ref _percentage, percentage, nameof(Percentage));
            Set(ref _percentageText, percentageText ?? string.Empty, nameof(PercentageText));
            Set(ref _clicked, clicked, nameof(Clicked));
        }

        void Set<T>(ref T field, T value, string propertyName)
        {
            if (Equals(field, value))
                return;
            field = value;
            RaisePropertyChanged<T>(propertyName);
        }
    }
}
