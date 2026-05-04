using System.Collections.Generic;
using System.Text;
using Graph.Apps.Antenna;
using Graph.Helpers;
using VRageMath;
using IMyRadioAntenna = Sandbox.ModAPI.IMyRadioAntenna;

namespace Graph.System.Antenna
{
    internal sealed class RadioAntennaCollector : AntennaCollector
    {
        public RadioAntennaCollector(AntennaSurfaceScript antennaSurfaceScript): base(antennaSurfaceScript)
        {
        }
        
        public override void Collect(GridLogic grid, List<AntennaEntry> entries)
        {
            var radios = grid.GetAntenna();

            for (int i = 0; i < radios.Count; i++)
            {
                var radio = radios[i];
                if(!IsValid(radio))
                    continue;

                entries.Add(new AntennaEntry
                {
                    Name = GetName(radio),
                    StatusIcon = GetStatusIcon(radio),
                    StatusText = GetStatusText(radio),
                    StatusColor = GetStatusColor(radio),
                    IsFunctional = radio.IsFunctional,
                    UseLaserIconCompensation = false
                });
            }
        }

        string GetName(IMyRadioAntenna radio)
        {
            try
            {
                return !string.IsNullOrWhiteSpace(radio.CustomName) ? radio.CustomName : radio.DisplayNameText;
            }
            catch
            {
                return "Radio Antenna";
            }
        }

        string GetStatusIcon(IMyRadioAntenna radioAntenna)
        {
            if (radioAntenna == null || !radioAntenna.Enabled)
                return "GridPower";

            if (!radioAntenna.IsFunctional)
                return "Warning";

            return radioAntenna.IsBroadcasting ? "RadioAntenna" : "RadioAntennaDisabled";
        }

        string GetStatusText(IMyRadioAntenna radioAntenna)
        {
            if (radioAntenna == null || !radioAntenna.Enabled)
                return GetLocCached("AssemblerState_Disabled");

            if (!radioAntenna.IsFunctional)
                return GetLocCached("Damaged");

            var sb = new StringBuilder();
            sb.AppendLine(string.IsNullOrWhiteSpace(radioAntenna.HudText)
                ? radioAntenna.CustomName
                : radioAntenna.HudText);
            sb.AppendLine(GetLocCached("BlockPropertyDescription_BroadcastRadius") + ": " +
                      FormatingHelper.DistanceToString(radioAntenna.Radius));
            sb.AppendLine(radioAntenna.IsBroadcasting
                ? GetLocCached("NotificationCharacterBroadcastingOn")
                : GetLocCached("NotificationCharacterBroadcastingOff"));
            return sb.ToString();
        }

        Color GetStatusColor(IMyRadioAntenna radioAntenna)
        {
            if (!radioAntenna.IsFunctional)
                return WarningColor;

            if (!radioAntenna.Enabled)
                return ForegroundColor;

            return radioAntenna.IsBroadcasting ? ForegroundColor : WarningColor;
        }
    }
}