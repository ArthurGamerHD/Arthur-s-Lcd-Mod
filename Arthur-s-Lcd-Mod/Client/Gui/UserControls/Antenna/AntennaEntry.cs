using LcdMod.Client.Gui.ControlsTemplates;
using VRageMath;

namespace LcdMod.Client.Gui.UserControls.Antenna
{
    internal sealed class AntennaEntry : ControlModelBase
    {
        public long EntryId { get; private set; }
        public string Name { get; private set; }
        public string StatusIcon { get; private set; }
        public string StatusText { get; private set; }
        public Color StatusColor { get; private set; }
        public bool UseLaserIconCompensation { get; private set; }
        public bool IsFunctional { get; private set; }
        public bool DrawAsLines { get; set; }

        public AntennaEntry(long entryId)
        {
            EntryId = entryId;
        }

        public void Update(
            string name,
            string statusIcon,
            string statusText,
            Color statusColor,
            bool isFunctional,
            bool useLaserIconCompensation)
        {
            Name = name ?? string.Empty;
            StatusIcon = statusIcon ?? string.Empty;
            StatusText = statusText ?? string.Empty;
            StatusColor = statusColor;
            IsFunctional = isFunctional;
            UseLaserIconCompensation = useLaserIconCompensation;
        }
    }
}
