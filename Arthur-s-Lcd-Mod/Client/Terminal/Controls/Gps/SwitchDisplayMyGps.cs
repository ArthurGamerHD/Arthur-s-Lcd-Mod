using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage.Utils;
using static LcdMod.Common.Helpers.Constants;

namespace LcdMod.Client.Terminal.Controls.Gps
{
    public sealed partial class SwitchDisplayMyGps : TerminalControlsWrapper
    {
        public override IMyTerminalControl TerminalControl { get; }

        public SwitchDisplayMyGps()
        {
            var toggle = CreateControl<IMyTerminalControlOnOffSwitch>("DisplayMyGps");
            toggle.Getter = Getter;
            toggle.Setter = Setter;
            toggle.Visible = Visible;
            toggle.Title = MyStringId.GetOrCompute(MOD_PREFIX + "Gps_DisplayMyGps_Title");
            toggle.Tooltip = MyStringId.GetOrCompute(
                MOD_PREFIX + "Gps_DisplayMyGps_Tooltip");
            toggle.OnText = MyStringId.GetOrCompute("HudInfoOn");
            toggle.OffText = MyStringId.GetOrCompute("HudInfoOff");
            TerminalControl = toggle;
        }

        protected override bool IsAvailableForCurrentConfig(IMyTerminalBlock block)
        {
            return GpsDisplayConfigHelper.GetConfig(block) != null &&
                   GpsDisplayConfigHelper.IsStaticMode(block);
        }

        void Setter(IMyTerminalBlock block, bool value)
        {
            GpsDisplayConfigHelper.Modify(block, config => config.DisplayMyGps = value);
        }

        bool Getter(IMyTerminalBlock block)
        {
            var config = GpsDisplayConfigHelper.GetConfig(block);
            return config != null && config.DisplayMyGps;
        }
    }
}
