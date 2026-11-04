using Adk.Mvvm;
using System;
using LcdMod.Client.Helpers;
using VRageMath;

namespace LcdMod.Client.Apps.CargoFilled
{
    internal sealed class CargoEntry : ObservableObject
    {
        public CargoEntry(long entryId)
        {
            EntryId = entryId;
        }

        public long EntryId { get; private set; }
        public string Icon { get; private set; }
        public double Cap { get; private set; }
        public string Name { get; private set; }
        public double Used { get; private set; }
        public bool RenderAsGrid { get; set; }
        public float Percentage => (float)MathHelper.Clamp(Cap <= 0d ? 0d : Used / Cap, 0d, 1d);
        public string PercentageText => FormatingHelper.PercentageToString(Percentage);
        public Action Clicked { get; set; }

        public void Update(string icon, string name, double used, double cap)
        {
            Icon = string.IsNullOrWhiteSpace(icon) ? "MissingIcon" : icon;
            Name = name ?? string.Empty;
            Used = used;
            Cap = cap;
        }
    }
}
