using System.Collections.Generic;
using System.Linq;
using LcdMod.Client.Config;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage.ModAPI;
using VRage.Utils;
using ScreenConfigColorable = LcdMod.Common.Config.Models.ScreenConfigColorable;

namespace LcdMod.Client.Terminal.Controls.Generic
{
    public sealed partial class ComboboxDisplayMode : TerminalControlsWrapper
    {
        public override IMyTerminalControl TerminalControl { get; }

        public ComboboxDisplayMode()
        {
            var slider = CreateControl<IMyTerminalControlCombobox>("ComboboxTable");
            slider.Getter = Getter;
            slider.ComboBoxContent = Content;
            slider.Setter = Setter;
            slider.Visible = Visible;
            slider.Title = MyStringId.GetOrCompute("DisplayName_Item_Display");

            TerminalControl = slider;
        }

        void Content(List<MyTerminalControlComboBoxItem> obj)
        {
            var displayMode = GetDisplayModeProvider();
            if (displayMode == null)
                return;

            obj.AddRange(displayMode.GetDisplayModes());
        }

        IMultiDisplayMode GetDisplayModeProvider()
        {
            var block = LcdModSessionComponent.LastSelectedBlock;
            if (block == null)
                return null;

            var surface = GetThisSurface(block);
            return ConfigManager.GetAppsForBlock(block)
                .FirstOrDefault(app => app.Surface.Equals(surface)) as IMultiDisplayMode;
        }

        void Setter(IMyTerminalBlock block, long l)
        {
            var config = ConfigManager.GetConfigForCurrentScreen(block) as ScreenConfigColorable;
            if (config == null)
                return;

            config.DisplayMode = (int)l;
            ConfigManager.Sync(block);
        }

        long Getter(IMyTerminalBlock block)
        {
            var config = ConfigManager.GetConfigForCurrentScreen(block) as ScreenConfigColorable;
            if (config == null)
                return 0;

            return config.DisplayMode;
        }
    }
}
