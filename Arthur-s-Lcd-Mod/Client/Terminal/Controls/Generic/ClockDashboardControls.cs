using System.Collections.Generic;
using LcdMod.Client.ClockDashboard;
using LcdMod.Client.Config;
using LcdMod.Common.Config.Components;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage.ModAPI;
using VRage.Utils;

namespace LcdMod.Client.Terminal.Controls.Generic
{
    public sealed partial class SwitchClockDashboard24Hour : TerminalControlsWrapper
    {
        public override IMyTerminalControl TerminalControl { get; }

        public SwitchClockDashboard24Hour()
        {
            var control = CreateControl<IMyTerminalControlOnOffSwitch>("ClockDashboard24Hour");
            control.Getter = Getter;
            control.Setter = Setter;
            control.Visible = Visible;
            control.Title = MyStringId.GetOrCompute(ClockDashboardLocalization.CONTROL_24_HOUR_TITLE_KEY);
            control.OnText = MyStringId.GetOrCompute("HudInfoOn");
            control.OffText = MyStringId.GetOrCompute("HudInfoOff");
            TerminalControl = control;
        }

        void Setter(IMyTerminalBlock block, bool value)
        {
            ConfigManager.ModifyComponentForTerminalApp<ClockDashboardConfigComponent>(
                block,
                config => config.Use24HourClock = value);
        }

        bool Getter(IMyTerminalBlock block)
        {
            var config = ConfigManager.GetComponentForTerminalApp<ClockDashboardConfigComponent>(block);
            return config == null || config.Use24HourClock;
        }
    }

    public sealed partial class ComboboxClockDashboardTemperatureMode : TerminalControlsWrapper
    {
        public override IMyTerminalControl TerminalControl { get; }

        public ComboboxClockDashboardTemperatureMode()
        {
            var control = CreateControl<IMyTerminalControlCombobox>("ClockDashboardTemperatureMode");
            control.Getter = Getter;
            control.Setter = Setter;
            control.ComboBoxContent = Content;
            control.Visible = Visible;
            control.Title = MyStringId.GetOrCompute(ClockDashboardLocalization.CONTROL_TEMPERATURE_TITLE_KEY);
            control.Tooltip = MyStringId.GetOrCompute(ClockDashboardLocalization.CONTROL_TEMPERATURE_TOOLTIP_KEY);
            TerminalControl = control;
        }

        static void Content(List<MyTerminalControlComboBoxItem> items)
        {
            items.Add(new MyTerminalControlComboBoxItem
            {
                Key = (long)ClockDashboardTemperatureMode.Fuzzy,
                Value = MyStringId.GetOrCompute(ClockDashboardLocalization.TEMPERATURE_FUZZY_KEY)
            });
            items.Add(new MyTerminalControlComboBoxItem
            {
                Key = (long)ClockDashboardTemperatureMode.Celsius,
                Value = MyStringId.GetOrCompute(ClockDashboardLocalization.TEMPERATURE_CELSIUS_KEY)
            });
            items.Add(new MyTerminalControlComboBoxItem
            {
                Key = (long)ClockDashboardTemperatureMode.Kelvin,
                Value = MyStringId.GetOrCompute(ClockDashboardLocalization.TEMPERATURE_KELVIN_KEY)
            });
            items.Add(new MyTerminalControlComboBoxItem
            {
                Key = (long)ClockDashboardTemperatureMode.Fahrenheit,
                Value = MyStringId.GetOrCompute(ClockDashboardLocalization.TEMPERATURE_FAHRENHEIT_KEY)
            });
        }

        static void Setter(IMyTerminalBlock block, long value)
        {
            ConfigManager.ModifyComponentForTerminalApp<ClockDashboardConfigComponent>(
                block,
                config => config.TemperatureModeInternal = (int)value);
        }

        static long Getter(IMyTerminalBlock block)
        {
            var config = ConfigManager.GetComponentForTerminalApp<ClockDashboardConfigComponent>(block);
            return config != null
                ? config.TemperatureModeInternal
                : (long)ClockDashboardTemperatureMode.Fuzzy;
        }
    }
}
