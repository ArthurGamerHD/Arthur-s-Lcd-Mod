using LcdMod.Client.Config;
using LcdMod.Common.Config.Components;
using LcdMod.Common.Helpers;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage.Utils;


namespace LcdMod.Client.Terminal.Controls.Color
{
    /// <summary>
    /// Color picker for warning color for many scripts.
    /// </summary>
    public sealed partial class ColorPickerWarning : TerminalControlsWrapper
    {
        public override IMyTerminalControl TerminalControl { get; }

        public ColorPickerWarning()
        {
            var colorPicker = CreateControl<IMyTerminalControlColor>("WarningColor");
            colorPicker.Getter = Getter;
            colorPicker.Setter = Setter;
            colorPicker.Visible = Visible;
            colorPicker.Title = MyStringId.GetOrCompute("SalvageService_InventoryWarning_Title");
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
                config => config.WarningColor.Set(color));
        }

        VRageMath.Color Getter(IMyTerminalBlock block)
        {
            return ConfigManager
                .GetComponentForCurrentSurface<ColorConfigComponent>(block, Constants.COLORS)
                .ResolveWarningColor();
        }
    }
}
