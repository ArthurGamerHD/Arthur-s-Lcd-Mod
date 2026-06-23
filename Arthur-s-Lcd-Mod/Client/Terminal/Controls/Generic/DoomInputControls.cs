using System.Text;
using LcdMod.Client.Config;
using LcdMod.Client.SurfaceScripts;
using ManagedDoom.SE;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage.Utils;

namespace LcdMod.Client.Terminal.Controls.Generic
{
    public sealed class SliderDoomKeyboardTurnSensitivity : TerminalControlsWrapper
    {
        public override IMyTerminalControl TerminalControl { get; }

        public SliderDoomKeyboardTurnSensitivity()
        {
            var slider = CreateControl<IMyTerminalControlSlider>(
                "SliderDoomKeyboardTurnSensitivity");
            slider.Getter = Getter;
            slider.Setter = Setter;
            slider.Visible = Visible;
            slider.SetLimits(
                DoomInputSettings.MinKeyboardTurnSensitivity,
                DoomInputSettings.MaxKeyboardTurnSensitivity);
            slider.Writer = Writer;
            slider.Title = MyStringId.GetOrCompute("Doom turn sensitivity");
            slider.Tooltip = MyStringId.GetOrCompute(
                "Turning speed of the A and D keys. 100% matches classic Doom keyboard turning.");
            TerminalControl = slider;
        }

        public override bool VisibleForScript(string script)
        {
            return script == ManagedDoomSurfaceScript.ID;
        }

        void Writer(IMyTerminalBlock block, StringBuilder builder)
        {
            builder.Append(((int)(Getter(block) + 0.5f)).ToString());
            builder.Append('%');
        }

        void Setter(IMyTerminalBlock block, float value)
        {
            var config = ConfigManager.GetConfigForCurrentScreen(block);
            if (config == null)
                return;

            DoomInputSettings.SetKeyboardTurnSensitivity(
                config,
                (int)(value + 0.5f));
            ConfigManager.Sync(block);
        }

        float Getter(IMyTerminalBlock block)
        {
            return DoomInputSettings.GetKeyboardTurnSensitivity(
                ConfigManager.GetConfigForCurrentScreen(block));
        }
    }

    public sealed class SwitchDoomMouseTurning : TerminalControlsWrapper
    {
        public override IMyTerminalControl TerminalControl { get; }

        public SwitchDoomMouseTurning()
        {
            var toggle = CreateControl<IMyTerminalControlOnOffSwitch>(
                "SwitchDoomMouseTurning");
            toggle.Getter = Getter;
            toggle.Setter = Setter;
            toggle.Visible = Visible;
            toggle.Title = MyStringId.GetOrCompute("Doom mouse turning");
            toggle.Tooltip = MyStringId.GetOrCompute(
                "Optionally add mouse horizontal movement to Doom turning. The Space Engineers camera is not locked or modified.");
            toggle.OnText = MyStringId.GetOrCompute("On");
            toggle.OffText = MyStringId.GetOrCompute("Off");
            TerminalControl = toggle;
        }

        public override bool VisibleForScript(string script)
        {
            return script == ManagedDoomSurfaceScript.ID;
        }

        void Setter(IMyTerminalBlock block, bool enabled)
        {
            var config = ConfigManager.GetConfigForCurrentScreen(block);
            if (config == null)
                return;

            DoomInputSettings.SetMouseTurningEnabled(config, enabled);
            ConfigManager.Sync(block);
        }

        bool Getter(IMyTerminalBlock block)
        {
            return DoomInputSettings.GetMouseTurningEnabled(
                ConfigManager.GetConfigForCurrentScreen(block));
        }
    }
}
