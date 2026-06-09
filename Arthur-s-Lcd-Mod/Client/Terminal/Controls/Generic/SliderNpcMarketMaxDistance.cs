using System;
using System.Text;
using LcdMod.Client.Config;
using LcdMod.Client.Helpers;
using LcdMod.Common.Config.Models.Apps;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage.Utils;

namespace LcdMod.Client.Terminal.Controls.Generic
{
    public sealed partial class SliderNpcMarketMaxDistance : TerminalControlsWrapper
    {
        public const float MIN_DISTANCE_METERS = 0f;
        public const float MAX_DISTANCE_METERS = 10000001f;
        public const float UNLIMITED_DISTANCE_METERS = MAX_DISTANCE_METERS;
        const float LOG_MIN_DISTANCE_METERS = 1f;

        public override IMyTerminalControl TerminalControl { get; }

        public SliderNpcMarketMaxDistance()
        {
            var slider = CreateControl<IMyTerminalControlSlider>("NpcMarketMaxDistanceSlider");
            slider.Getter = Getter;
            slider.Setter = Setter;
            slider.Visible = Visible;
            slider.SetLogLimits(LOG_MIN_DISTANCE_METERS, MAX_DISTANCE_METERS);
            slider.Writer = Writer;
            slider.Title = MyStringId.GetOrCompute("Maximum distance");

            TerminalControl = slider;
        }

        void Writer(IMyTerminalBlock block, StringBuilder text)
        {
            var distance = GetDistanceMeters(Getter(block));
            if (IsUnlimited(distance))
            {
                text.Append("Unlimited");
                return;
            }

            text.Append(FormatingHelper.DistanceToString(distance));
        }

        void Setter(IMyTerminalBlock block, float value)
        {
            var config = ConfigManager.GetConfigForCurrentScreen(block) as ScreenConfigNpcMarket;
            if (config == null)
                return;

            config.MaxDistanceMeters = ClampDistanceMeters(value);
            ConfigManager.Sync(block);
        }

        float Getter(IMyTerminalBlock block)
        {
            var config = ConfigManager.GetConfigForCurrentScreen(block) as ScreenConfigNpcMarket;
            return config != null ? GetSliderValue(config.MaxDistanceMeters) : UNLIMITED_DISTANCE_METERS;
        }

        public static float ClampDistanceMeters(float meters)
        {
            if (float.IsNaN(meters) || float.IsInfinity(meters))
                return UNLIMITED_DISTANCE_METERS;

            if (meters >= UNLIMITED_DISTANCE_METERS)
                return UNLIMITED_DISTANCE_METERS;

            return Math.Max(MIN_DISTANCE_METERS, meters);
        }

        public static float GetDistanceMeters(float value)
        {
            return ClampDistanceMeters(value);
        }

        static float GetSliderValue(float meters)
        {
            var value = ClampDistanceMeters(meters);
            if (value <= MIN_DISTANCE_METERS)
                return LOG_MIN_DISTANCE_METERS;

            return value;
        }

        public static bool IsUnlimited(float meters)
        {
            return float.IsNaN(meters) || float.IsInfinity(meters) || meters >= UNLIMITED_DISTANCE_METERS;
        }
    }
}
