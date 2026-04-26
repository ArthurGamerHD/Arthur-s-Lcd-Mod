using Generated;
using Graph.System.Config;
using Graph.System.TerminalControls.Color;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage;
using VRage.Utils;

namespace Graph.System.TerminalControls.Color
{
    public partial class SwitchToggleColors : TerminalControlsWrapper
    {
        public override IMyTerminalControl TerminalControl { get; }
        
        public SwitchToggleColors()
        {
            var slider = CreateControl<IMyTerminalControlOnOffSwitch>("SwitchToggleCustomColors");
            slider.Getter = Getter;
            slider.Setter = Setter;
            slider.Visible = Visible;
            slider.Title = MyStringId.GetOrCompute(
                $"{MyTexts.Get(MyStringId.GetOrCompute("WorldSettings_ViewDistance_Custom"))} " +
                $"{MyTexts.Get(MyStringId.GetOrCompute("ScreenAdmin_Safezone_ColorLabel"))}");
            
            slider.OnText = MyStringId.GetOrCompute("HudInfoOn");
            slider.OffText = MyStringId.GetOrCompute("HudInfoOff");

            TerminalControl = slider;
        }

        void Setter(IMyTerminalBlock block, bool value)
        {
            var screen = GetThisSurfaceIndex(block);
            var config = ConfigManager.GetConfigForScreen(block, screen);

            if (config == null)
                return;

            config.CustomizedColors = value;

            ConfigManager.Sync(block);

            var surface = (block as IMyTextSurfaceProvider)?.GetSurface(screen);
            if (surface != null)
            {
                var script = surface.Script;
                surface.Script = "None";
                surface.Script = script;
            }
        }

        bool Getter(IMyTerminalBlock myTerminalBlock)
        {
            var config = ConfigManager.GetConfigForCurrentScreen(myTerminalBlock);
            return config != null && config.CustomizedColors;
        }
    }
}
