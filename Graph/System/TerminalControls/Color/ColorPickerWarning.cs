using Graph.System.Config;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage.Utils;

namespace Graph.System.TerminalControls.Color
{
    /// <summary>
    /// Color picker for Error for many Scripts using <see cref="ScreenConfig"/> 
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
            var config = ConfigManager.GetConfigForCurrentScreen(block);
            return (config?.CustomizedColors ?? false) && base.Visible(block);
        }

        void Setter(IMyTerminalBlock block, VRageMath.Color color)
        {
            var config = ConfigManager.GetConfigForCurrentScreen(block);
            if(config == null)
                return;
            config.WarningColor = color;
            ConfigManager.Sync(block);
        }

        VRageMath.Color Getter(IMyTerminalBlock block)
        {
            var config = ConfigManager.GetConfigForCurrentScreen(block);
            if (config?.WarningColor != null)
                return config.WarningColor;
            
            return VRageMath.Color.White;
        }
    }
}
