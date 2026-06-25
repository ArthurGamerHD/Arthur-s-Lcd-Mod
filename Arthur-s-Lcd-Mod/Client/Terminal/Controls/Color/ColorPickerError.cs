using LcdMod.Client.Config;
using LcdMod.Common.Config.Components;
using LcdMod.Common.Helpers;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage.Utils;


namespace LcdMod.Client.Terminal.Controls.Color
{
    /// <summary>
    /// Color picker for error color for many scripts.
    /// </summary>
    public sealed partial class ColorPickerError : TerminalControlsWrapper
    {
        public override IMyTerminalControl TerminalControl { get; }

        public ColorPickerError()
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
            var config = ConfigManager.GetComponentForCurrentSurface<ColorConfigComponent>(block, Constants.COLORS);
            return (config?.CustomizedColors ?? false) && base.Visible(block);
        }

        void Setter(IMyTerminalBlock block, VRageMath.Color color)
        {
            ConfigManager.ModifyComponentForCurrentSurface<ColorConfigComponent>(
                block,
                Constants.COLORS,
                config => config.ErrorColor.Set(color));
        }

        VRageMath.Color Getter(IMyTerminalBlock block)
        {
            return ConfigManager
                .GetComponentForCurrentSurface<ColorConfigComponent>(block, Constants.COLORS)
                .ResolveErrorColor();
        }
    }
}
