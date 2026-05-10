using LcdMod.Client.Config;
using LcdMod.Common.Config.Models;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage.Utils;
using ScreenConfigColorable = LcdMod.Common.Config.Models.ScreenConfigColorable;

namespace LcdMod.Client.Terminal.Controls.Color
{
    /// <summary>
    /// Color picker for Error for many Scripts using <see cref="ScreenConfigGeneral"/> 
    /// </summary>
    public sealed partial class ColorPickerWarning : TerminalControlsWrapper
    {
        public override IMyTerminalControl TerminalControl { get; }

        public ColorPickerWarning()
        {
            var colorPicker = CreateControl<IMyTerminalControlColor>("ErrorColor");
            colorPicker.Getter = Getter;
            colorPicker.Setter = Setter;
            colorPicker.Visible = Visible;
            colorPicker.Title = MyStringId.GetOrCompute("ContractScreen_Aministration_CreatinResultCaption_Error");
            TerminalControl = colorPicker;
        }

        public override bool Visible(IMyTerminalBlock block)
        {
            var config = ConfigManager.GetConfigForCurrentScreen(block) as ScreenConfigColorable;
            return (config?.CustomizedColors ?? false) && base.Visible(block);
        }

        void Setter(IMyTerminalBlock block, VRageMath.Color color)
        {
            var config = ConfigManager.GetConfigForCurrentScreen(block) as ScreenConfigColorable;
            if (config == null)
                return;
            config.WarningColor = color;
            ConfigManager.Sync(block);
        }

        VRageMath.Color Getter(IMyTerminalBlock block)
        {
            var config = ConfigManager.GetConfigForCurrentScreen(block) as ScreenConfigColorable;
            return config == null ? VRageMath.Color.White : config.WarningColor;
        }
    }
}
