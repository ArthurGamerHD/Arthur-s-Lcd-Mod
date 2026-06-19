using LcdMod.Client.Config;
using LcdMod.Common.Config.Models.Apps;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage.Utils;
using static LcdMod.Common.Helpers.Constants;

namespace LcdMod.Client.Terminal.Controls.Proxy
{
    public sealed partial class SwitchProxyAutoAdjust : TerminalControlsWrapper
    {
        public override IMyTerminalControl TerminalControl { get; }

        public SwitchProxyAutoAdjust()
        {
            var slider = CreateControl<IMyTerminalControlOnOffSwitch>("ProxyAutoAdjustSwitch");
            slider.Getter = Getter;
            slider.Setter = Setter;
            slider.Visible = Visible;
            slider.Title = MyStringId.GetOrCompute(MOD_PREFIX + "EnableAutoAdjust");
            slider.Tooltip = MyStringId.GetOrCompute(MOD_PREFIX + "EnableAutoAdjust_Tooltip");
            slider.OnText = MyStringId.GetOrCompute("HudInfoOn");
            slider.OffText = MyStringId.GetOrCompute("HudInfoOff");

            TerminalControl = slider;
        }

        void Setter(IMyTerminalBlock block, bool value)
        {
            var config = ConfigManager.GetConfigForCurrentScreen(block) as ScreenConfigRenderProxy;
            if (config == null)
                return;

            config.EnableAutoAdjust = value;
            ConfigManager.Sync(block);
        }

        bool Getter(IMyTerminalBlock block)
        {
            var config = ConfigManager.GetConfigForCurrentScreen(block) as ScreenConfigRenderProxy;
            return config != null && config.EnableAutoAdjust;
        }
    }
}
