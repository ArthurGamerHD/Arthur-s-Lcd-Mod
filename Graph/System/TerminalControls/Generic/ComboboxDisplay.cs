using System.Collections.Generic;
using Graph.Apps.Antenna;
using Graph.Apps.Diagnostic;
using Graph.Apps.Inventory;
using Graph.Apps.Percentage;
using Graph.Apps.Power;
using Graph.Apps.Refinery;
using Graph.System.Config;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage.ModAPI;
using VRage.Utils;

namespace Graph.System.TerminalControls.Generic
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
            if (LcdModSessionComponent.LastSelected == IntegrityMonitorSurfaceScript.ID)
            {
                obj.Add(new MyTerminalControlComboBoxItem
                {
                    Key = 0,
                    Value = MyStringId.GetOrCompute("LCDMod_Axis_X_Positive")
                });

                obj.Add(new MyTerminalControlComboBoxItem
                {
                    Key = 1,
                    Value = MyStringId.GetOrCompute("LCDMod_Axis_X_Negative")
                });


                obj.Add(new MyTerminalControlComboBoxItem
                {
                    Key = 2,
                    Value = MyStringId.GetOrCompute("LCDMod_Axis_Y_Positive")
                });

                obj.Add(new MyTerminalControlComboBoxItem
                {
                    Key = 3,
                    Value = MyStringId.GetOrCompute("LCDMod_Axis_Y_Negative")
                });

                obj.Add(new MyTerminalControlComboBoxItem
                {
                    Key = 4,
                    Value = MyStringId.GetOrCompute("LCDMod_Axis_Z_Positive")
                });

                obj.Add(new MyTerminalControlComboBoxItem
                {
                    Key = 5,
                    Value = MyStringId.GetOrCompute("LCDMod_Axis_Z_Negative")
                });
                return;
            }
            
            obj.Add(new MyTerminalControlComboBoxItem
            {
                Key = 0,
                Value = MyStringId.GetOrCompute("LCD_Grid")
            });
            
            obj.Add(new MyTerminalControlComboBoxItem
            {
                Key = 1,
                Value = MyStringId.GetOrCompute("StoryTitle_MinerStories12")
            });

        }

        void Setter(IMyTerminalBlock block, long l)
        {
            var config = ConfigManager.GetConfigForCurrentScreen(block);
            if (config == null)
                return;

            config.DisplayInternal = (int)l;
            ConfigManager.Sync(block);
        }

        long Getter(IMyTerminalBlock block)
        {
            var config = ConfigManager.GetConfigForCurrentScreen(block);
            if (config == null)
                return 0;

            return config.DisplayInternal;
        }
    }
}
