using Adk.Gui.Core.Input;
using System;
using LcdMod.Common.Mvvm;
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

        public long Key { get; private set; }
        public string Icon => _icon;
        public string Name => _name;
        public float Percentage => _percentage;
        public string PercentageText => _percentageText;
        public Action Clicked => _clicked;
        public string Classes => _classes;
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
