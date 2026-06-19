using ProtoBuf;

namespace LcdMod.Common.Config.Models.Apps
{
    public enum ClockDashboardTemperatureMode
    {
        Fuzzy = 0,
        Celsius = 1,
        Kelvin = 2,
        Fahrenheit = 3
    }

    [ProtoContract]
    public partial class ScreenConfigClockDashboard : ScreenConfigInteractive
    {
        public override int Id => 23;

        [ProtoMember(39)] public bool Use24HourClock { get; set; } = true;
        [ProtoMember(40)] public int TemperatureModeInternal { get; set; } = (int)ClockDashboardTemperatureMode.Fuzzy;

        public ClockDashboardTemperatureMode TemperatureMode => (ClockDashboardTemperatureMode)TemperatureModeInternal;
    }
}
