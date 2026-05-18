using System;
using System.Text;
using LcdMod.Client.Config;

using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage.Utils;
using VRageMath;
using ScreenConfigStarMap = LcdMod.Common.Config.Models.Apps.ScreenConfigStarMap;

namespace LcdMod.Client.Terminal.Controls.Generic
{
    public sealed partial class SliderFov : TerminalControlsWrapper
    {
        public override IMyTerminalControl TerminalControl { get; }
        const float BASE_FOV_DEG = 70f;
        const float MIN_MAG = 0.4f;
        const float MAX_MAG = 80f;
        const float MIN_FOV_DEG = 0.1f;
        const float MAX_FOV_DEG = 120f;

        public SliderFov()
        {
            var slider = CreateControl<IMyTerminalControlSlider>("SliderFov");
            slider.Getter = Getter;
            slider.Setter = Setter;
            slider.Visible = Visible;
            slider.SetLimits(MIN_MAG, MAX_MAG);
            slider.Writer = Writer;
            slider.Title = MyStringId.GetOrCompute("Magnification");

            TerminalControl = slider;
        }

        void Writer(IMyTerminalBlock b, StringBuilder arg2)
        {
            arg2.Append(Getter(b).ToString("0.##") + "x");
        }

        void Setter(IMyTerminalBlock block, float value)
        {
            var config = ConfigManager.GetConfigForCurrentScreen(block) as ScreenConfigStarMap;
            if (config == null)
                return;

            float mag = MathHelper.Clamp(value, MIN_MAG, MAX_MAG);
            config.FoV = MagnificationToFov(mag);
            ConfigManager.Sync(block);
        }

        float Getter(IMyTerminalBlock block)
        {
            var config = ConfigManager.GetConfigForCurrentScreen(block) as ScreenConfigStarMap;
            if (config == null)
                return 1f;

            return FovToMagnification(config.FoV);
        }

        public static float MagnificationToFov(float magnification)
        {
            double baseHalfFov = MathHelper.ToRadians(BASE_FOV_DEG) * 0.5;
            double halfFov = Math.Atan(Math.Tan(baseHalfFov) / Math.Max(0.0001f, magnification));
            float fov = (float)(MathHelper.ToDegrees(halfFov) * 2d);
            return MathHelper.Clamp(fov, MIN_FOV_DEG, MAX_FOV_DEG);
        }

        public static float FovToMagnification(float fovDeg)
        {
            double baseHalfFov = MathHelper.ToRadians(BASE_FOV_DEG) * 0.5;
            double currentHalfFov = MathHelper.ToRadians(MathHelper.Clamp(fovDeg, MIN_FOV_DEG, MAX_FOV_DEG)) * 0.5;
            double magnification = Math.Tan(baseHalfFov) / Math.Tan(currentHalfFov);
            return MathHelper.Clamp((float)magnification, MIN_MAG, MAX_MAG);
        }
    }
}
