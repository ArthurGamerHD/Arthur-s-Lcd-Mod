using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage.Utils;

namespace LcdMod.Client.Terminal.Controls.Gps
{
    public sealed partial class SwitchIncludeRadioSignals : TerminalControlsWrapper
    {
        public override IMyTerminalControl TerminalControl { get; }

        public SwitchIncludeRadioSignals()
        {
            var toggle = CreateControl<IMyTerminalControlOnOffSwitch>("IncludeRadioSignals");
            toggle.Getter = Getter;
            toggle.Setter = Setter;
            toggle.Visible = Visible;
            toggle.Title = MyStringId.GetOrCompute("Include Radio Signals");
            toggle.Tooltip = MyStringId.GetOrCompute(
                "Display active radio antenna, beacon, and connected laser antenna signals that reach this grid on the static map.");
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
            GpsDisplayConfigHelper.Modify(block, config => config.IncludeRadioSignals = value);
        }

        bool Getter(IMyTerminalBlock block)
        {
            var config = GpsDisplayConfigHelper.GetConfig(block);
            return config != null && config.IncludeRadioSignals;
        }
    }
}
