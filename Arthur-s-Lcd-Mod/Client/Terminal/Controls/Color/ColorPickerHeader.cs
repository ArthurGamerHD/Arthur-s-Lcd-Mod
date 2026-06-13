using LcdMod.Client.Config;
using LcdMod.Common.Config.Models;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage.Utils;


namespace LcdMod.Client.Terminal.Controls.Color
{
    /// <summary>
    /// Color picker for Header for many Scripts using <see cref="ScreenConfigGeneral"/> 
    /// </summary>
    public sealed partial class ColorPickerAccent : TerminalControlsWrapper
    {
        public override IMyTerminalControl TerminalControl { get; }

        public ColorPickerAccent()
        {
            var colorPicker = CreateControl<IMyTerminalControlColor>("HeaderColor");
            colorPicker.Getter = Getter;
            colorPicker.Setter = Setter;
            colorPicker.Visible = Visible;
            colorPicker.Title = MyStringId.GetOrCompute("BlockPropertyTitle_TextPanelPublicTitle");
            TerminalControl = colorPicker;
        }

        public override bool Visible(IMyTerminalBlock block)
        {
            var config = ConfigManager.GetConfigForCurrentScreen(block);
            return (config?.CustomizedColors ?? false) && base.Visible(block);
        }

        void Setter(IMyTerminalBlock block, VRageMath.Color color)
        {
            var config = ConfigManager.GetConfigForCurrentScreen(block);
            if (config == null)
                return;
            config.HeaderColor = color;
            ConfigManager.Sync(block);
        }

        VRageMath.Color Getter(IMyTerminalBlock block)
        {
            var config = ConfigManager.GetConfigForCurrentScreen(block);
            return config?.HeaderColor ?? VRageMath.Color.White;
        }
    }
}
